using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Protocol;
using NowPlayingOverlay.Host.Shell;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayHttpServer : IAsyncDisposable
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    private readonly object _gate = new();
    private readonly HostOptions _options;
    private readonly OverlayPageAsset _pageAsset;
    private readonly BrowserProducerAsset _browserProducerAsset;
    private readonly NowPlayingStore _store;
    private readonly ArtworkCache _artworkCache;
    private readonly HostHealthService _healthService;
    private readonly AppearanceState _appearanceState;
    private readonly ConnectionLimiter _sseLimiter;
    private readonly ConnectionLimiter _requestLimiter;
    private readonly ServerEndpointChangeBroadcaster _endpointChanges;
    private readonly SpotifyAuthorizationCallbackBroker _spotifyCallbackBroker;
    private readonly ExternalIngestHttpHandler? _externalIngestHandler;
    private readonly ILogger<OverlayHttpServer> _logger;
    private readonly SemaphoreSlim _rebindGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<ListenerEndpoint> _endpoints = [];
    private readonly HashSet<Task> _retirements = [];
    private ListenerEndpoint? _currentEndpoint;
    private bool _started;
    private bool _disposed;

    public OverlayHttpServer(
        HostOptions options,
        OverlayPageAsset pageAsset,
        NowPlayingStore store,
        ArtworkCache artworkCache,
        HostHealthService healthService,
        AppearanceState appearanceState,
        ConnectionLimiter sseLimiter,
        ConnectionLimiter requestLimiter,
        ServerEndpointChangeBroadcaster endpointChanges,
        SpotifyAuthorizationCallbackBroker spotifyCallbackBroker,
        ExternalIngestHttpHandler? externalIngestHandler,
        ILogger<OverlayHttpServer> logger,
        BrowserProducerAsset? browserProducerAsset = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pageAsset = pageAsset ?? throw new ArgumentNullException(nameof(pageAsset));
        _browserProducerAsset = browserProducerAsset
            ?? BrowserProducerAsset.LoadEmbedded(typeof(BrowserProducerAsset).Assembly);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _artworkCache = artworkCache ?? throw new ArgumentNullException(nameof(artworkCache));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _appearanceState = appearanceState ?? throw new ArgumentNullException(nameof(appearanceState));
        _sseLimiter = sseLimiter ?? throw new ArgumentNullException(nameof(sseLimiter));
        _requestLimiter = requestLimiter ?? throw new ArgumentNullException(nameof(requestLimiter));
        _endpointChanges = endpointChanges ?? throw new ArgumentNullException(nameof(endpointChanges));
        _spotifyCallbackBroker = spotifyCallbackBroker
            ?? throw new ArgumentNullException(nameof(spotifyCallbackBroker));
        _externalIngestHandler = externalIngestHandler;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int CurrentPort
    {
        get
        {
            lock (_gate)
            {
                return _currentEndpoint?.Port ?? _options.Port;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The HTTP server has already started.");
            }

            var endpoint = CreateEndpoint(_options.Port);
            try
            {
                endpoint.Start();
            }
            catch
            {
                endpoint.CloseAfterFailedStart();
                throw;
            }
            _endpoints.Add(endpoint);
            _currentEndpoint = endpoint;
            _started = true;
        }

        _logger.LogInformation("Listening on http://127.0.0.1:{Port}.", _options.Port);
        return Task.CompletedTask;
    }

    public async Task RebindAsync(
        int newPort,
        Action persistPort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persistPort);
        if (newPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(newPort));
        }

        await _rebindGate.WaitAsync(cancellationToken);
        try
        {
            ListenerEndpoint previous;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_started || _currentEndpoint is null)
                {
                    throw new InvalidOperationException("The HTTP server is not running.");
                }

                if (_currentEndpoint.Port == newPort)
                {
                    return;
                }

                previous = _currentEndpoint;
            }

            var candidate = CreateEndpoint(newPort);
            try
            {
                candidate.Start();
            }
            catch
            {
                candidate.CloseAfterFailedStart();
                throw;
            }
            try
            {
                // Persist only after the new prefix is live; a save failure must leave the old endpoint authoritative.
                persistPort();
            }
            catch
            {
                await candidate.StopAsync();
                throw;
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _endpoints.Add(candidate);
                _currentEndpoint = candidate;
            }

            var overlayUrl = TrayMenuController.BuildOverlayUrl(newPort);
            _endpointChanges.Publish(overlayUrl);
            _logger.LogInformation(
                "Moved the loopback endpoint from port {OldPort} to {NewPort}; the old endpoint remains during the migration grace period.",
                previous.Port,
                newPort);
            TrackRetirement(RetireEndpointAsync(previous, _options.PortRebindGracePeriod));
        }
        finally
        {
            _rebindGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _rebindGate.WaitAsync(cancellationToken);
        try
        {
            ListenerEndpoint[] endpoints;
            Task[] retirements;
            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                _shutdown.Cancel();
                endpoints = _endpoints.ToArray();
                retirements = _retirements.ToArray();
            }

            await Task.WhenAll(endpoints.Select(endpoint => endpoint.StopAsync()));
            try
            {
                await Task.WhenAll(retirements).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
        }
        finally
        {
            _rebindGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync();
        _externalIngestHandler?.Dispose();
        _shutdown.Dispose();
        _rebindGate.Dispose();
    }

    private ListenerEndpoint CreateEndpoint(int port)
    {
        return new ListenerEndpoint(port, _options, HandleContextAsync, _logger);
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;
        try
        {
            PrepareCommonResponse(response);
            if (!_requestLimiter.TryAcquire(out var requestLease))
            {
                response.StatusCode = 503;
                response.Headers[HttpResponseHeader.RetryAfter] = "1";
                response.ContentLength64 = 0;
                return;
            }

            using (requestLease)
            {
                if (!HasAllowedHeaders(context.Request))
                {
                    response.StatusCode = 431;
                    response.ContentLength64 = 0;
                    return;
                }

                if (!HasAllowedHost(context.Request))
                {
                    response.StatusCode = 400;
                    response.ContentLength64 = 0;
                    return;
                }

                await RouteAsync(context, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or HttpListenerException)
        {
            _logger.LogDebug(error, "The loopback client disconnected while a response was in progress.");
        }
        catch (Exception error)
        {
            _logger.LogError(error, "The loopback HTTP request failed.");
            TrySetInternalServerError(response);
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException)
            {
            }
        }
    }

    private async Task RouteAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? string.Empty;
        if (string.Equals(
            path,
            SpotifyAuthorizationRequest.RedirectPath,
            StringComparison.Ordinal))
        {
            await WriteSpotifyAuthorizationCallbackAsync(context, cancellationToken);
            return;
        }

        if (string.Equals(path, "/NowPlaying.html", StringComparison.Ordinal))
        {
            await HandleGetAndHeadAsync(
                context,
                "text/html; charset=utf-8",
                "no-store",
                _pageAsset.Bytes,
                cancellationToken,
                ContentSecurityPolicy);
            return;
        }

        if (string.Equals(path, BrowserProducerAsset.Path, StringComparison.Ordinal))
        {
            await HandleGetAndHeadAsync(
                context,
                "application/javascript; charset=utf-8",
                "no-store",
                _browserProducerAsset.Bytes,
                cancellationToken);
            return;
        }

        if (string.Equals(path, "/api/v3/state", StringComparison.Ordinal))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                NowPlayingStateMapper.Map(_store.Current),
                ProtocolJson.Options);
            await HandleGetAndHeadAsync(
                context,
                "application/json; charset=utf-8",
                "no-store",
                bytes,
                cancellationToken);
            return;
        }

        if (string.Equals(path, "/api/v3/appearance", StringComparison.Ordinal))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                AppearanceDtoMapper.Map(_appearanceState.GetCurrent()),
                ProtocolJson.Options);
            await HandleGetAndHeadAsync(
                context,
                "application/json; charset=utf-8",
                "no-store",
                bytes,
                cancellationToken);
            return;
        }

        const string artworkPrefix = "/api/v3/artwork/";
        if (path.StartsWith(artworkPrefix, StringComparison.Ordinal))
        {
            await WriteArtworkAsync(context, path[artworkPrefix.Length..], cancellationToken);
            return;
        }

        if (string.Equals(path, "/api/v3/events", StringComparison.Ordinal))
        {
            if (!IsMethod(request, "GET"))
            {
                WriteMethodNotAllowed(context.Response, "GET");
                return;
            }

            await WriteEventsAsync(context, cancellationToken);
            return;
        }

        if (string.Equals(path, "/health", StringComparison.Ordinal))
        {
            var health = _healthService.GetHealth();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(health.Body, ProtocolJson.Options);
            await HandleGetAndHeadAsync(
                context,
                "application/json; charset=utf-8",
                "no-store",
                bytes,
                cancellationToken,
                statusCode: health.StatusCode);
            return;
        }

        if (_externalIngestHandler is not null
            && (string.Equals(path, ExternalIngestHttpHandler.StatePath, StringComparison.Ordinal)
                || string.Equals(path, ExternalIngestHttpHandler.HeartbeatPath, StringComparison.Ordinal)))
        {
            await _externalIngestHandler.HandleAsync(
                context,
                heartbeat: string.Equals(
                    path,
                    ExternalIngestHttpHandler.HeartbeatPath,
                    StringComparison.Ordinal),
                cancellationToken);
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.ContentLength64 = 0;
    }

    private async Task WriteSpotifyAuthorizationCallbackAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        var response = context.Response;
        if (!_spotifyCallbackBroker.HasPendingAuthorization)
        {
            response.StatusCode = 404;
            response.ContentLength64 = 0;
            return;
        }

        if (!IsMethod(context.Request, "GET"))
        {
            WriteMethodNotAllowed(response, "GET");
            return;
        }

        if (context.Request.Url is not Uri callbackUri
            || !_spotifyCallbackBroker.TryComplete(callbackUri, out var callbackResponse))
        {
            response.StatusCode = 404;
            response.ContentLength64 = 0;
            return;
        }

        var body = Encoding.UTF8.GetBytes(
            $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Now Playing Overlay</title></head><body><p>{WebUtility.HtmlEncode(callbackResponse.Message)}</p></body></html>");
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        response.StatusCode = (int)callbackResponse.StatusCode;
        await WriteBodyAsync(
            context,
            "text/html; charset=utf-8",
            "no-store",
            body,
            cancellationToken);
    }

    private async Task WriteArtworkAsync(
        HttpListenerContext context,
        string artworkId,
        CancellationToken cancellationToken)
    {
        if (!IsGetOrHead(context.Request))
        {
            WriteMethodNotAllowed(context.Response, "GET, HEAD");
            return;
        }

        if (!IsLowercaseSha256(artworkId) || !_artworkCache.TryGet(artworkId, out var entry))
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength64 = 0;
            return;
        }

        await WriteBodyAsync(
            context,
            entry!.ContentType,
            "public, max-age=31536000, immutable",
            entry.Bytes,
            cancellationToken);
    }

    private async Task WriteEventsAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        if (!_sseLimiter.TryAcquire(out var lease))
        {
            context.Response.StatusCode = 503;
            context.Response.Headers[HttpResponseHeader.RetryAfter] = "1";
            context.Response.ContentLength64 = 0;
            return;
        }

        using (lease)
        // Reconnects always receive the current snapshot; Last-Event-ID never replays history.
        using (var subscription = _store.Subscribe())
        using (var endpointSubscription = _endpointChanges.Subscribe())
        using (var heartbeat = new PeriodicTimer(_options.SseHeartbeatInterval))
        {
            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            response.SendChunked = true;
            response.KeepAlive = true;

            var waitForSnapshot = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var waitForHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
            var waitForEndpoint = endpointSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            while (!cancellationToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(waitForSnapshot, waitForHeartbeat, waitForEndpoint);
                if (completed == waitForSnapshot)
                {
                    if (!await waitForSnapshot)
                    {
                        break;
                    }

                    // The subscription is capacity one; draining publishes only the newest state.
                    while (subscription.Reader.TryRead(out var snapshot))
                    {
                        await WriteSseSnapshotAsync(response, snapshot, cancellationToken);
                    }

                    waitForSnapshot = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                }

                if (completed == waitForEndpoint)
                {
                    if (!await waitForEndpoint)
                    {
                        break;
                    }

                    string? overlayUrl = null;
                    while (endpointSubscription.Reader.TryRead(out var changedUrl))
                    {
                        overlayUrl = changedUrl;
                    }

                    if (overlayUrl is not null)
                    {
                        await WriteServerEndpointAsync(response, overlayUrl, cancellationToken);
                        break;
                    }

                    waitForEndpoint = endpointSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                }

                if (completed == waitForHeartbeat || waitForHeartbeat.IsCompletedSuccessfully)
                {
                    if (!await waitForHeartbeat)
                    {
                        break;
                    }

                    await WriteUtf8Async(response, ": heartbeat\n\n", cancellationToken);
                    waitForHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                }
            }
        }
    }

    private static async Task HandleGetAndHeadAsync(
        HttpListenerContext context,
        string contentType,
        string cacheControl,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken,
        string? contentSecurityPolicy = null,
        int statusCode = 200)
    {
        if (!IsGetOrHead(context.Request))
        {
            WriteMethodNotAllowed(context.Response, "GET, HEAD");
            return;
        }

        if (contentSecurityPolicy is not null)
        {
            context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
        }

        context.Response.StatusCode = statusCode;
        await WriteBodyAsync(context, contentType, cacheControl, bytes, cancellationToken);
    }

    private static async Task WriteBodyAsync(
        HttpListenerContext context,
        string contentType,
        string cacheControl,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = contentType;
        context.Response.Headers[HttpResponseHeader.CacheControl] = cacheControl;
        context.Response.ContentLength64 = bytes.Length;
        if (!IsMethod(context.Request, "HEAD"))
        {
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        }
    }

    private static async Task WriteSseSnapshotAsync(
        HttpListenerResponse response,
        NowPlayingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var data = ProtocolJson.Serialize(NowPlayingStateMapper.Map(snapshot));
        await WriteUtf8Async(
            response,
            $"event: state\nid: {snapshot.ServerInstanceId:D}:{snapshot.SnapshotRevision}\ndata: {data}\n\n",
            cancellationToken);
    }

    private static Task WriteServerEndpointAsync(
        HttpListenerResponse response,
        string overlayUrl,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(new ServerEndpointDto(overlayUrl), ProtocolJson.Options);
        return WriteUtf8Async(response, $"event: server\ndata: {data}\n\n", cancellationToken);
    }

    private static async Task WriteUtf8Async(
        HttpListenerResponse response,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        await response.OutputStream.FlushAsync(cancellationToken);
    }

    private bool HasAllowedHeaders(HttpListenerRequest request)
    {
        if (request.Headers.Count > _options.MaximumRequestHeaderCount)
        {
            return false;
        }

        var totalBytes = 0;
        foreach (var name in request.Headers.AllKeys)
        {
            if (name is null)
            {
                continue;
            }

            totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(name) + 2);
            foreach (var value in request.Headers.GetValues(name) ?? [])
            {
                totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(value) + 2);
            }

            if (totalBytes > _options.MaximumRequestHeadersTotalSize)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAllowedHost(HttpListenerRequest request)
    {
        var host = request.Headers["Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var separator = host.LastIndexOf(':');
        var hostName = separator < 0 ? host : host[..separator];
        var port = separator < 0 ? null : host[(separator + 1)..];
        return string.Equals(hostName, HostOptions.AllowedHost, StringComparison.Ordinal)
            && (port is null || int.TryParse(port, out var parsedPort) && parsedPort is >= 1 and <= 65535);
    }

    private static void PrepareCommonResponse(HttpListenerResponse response)
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers[HttpResponseHeader.Server] = string.Empty;
    }

    private static void WriteMethodNotAllowed(HttpListenerResponse response, string allow)
    {
        response.StatusCode = 405;
        response.Headers[HttpResponseHeader.Allow] = allow;
        response.ContentLength64 = 0;
    }

    private static void TrySetInternalServerError(HttpListenerResponse response)
    {
        try
        {
            response.StatusCode = 500;
            response.ContentLength64 = 0;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void TrackRetirement(Task retirement)
    {
        lock (_gate)
        {
            _retirements.Add(retirement);
        }

        _ = retirement.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    _retirements.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RetireEndpointAsync(ListenerEndpoint endpoint, TimeSpan gracePeriod)
    {
        try
        {
            await Task.Delay(gracePeriod, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }

        await endpoint.StopAsync();
        lock (_gate)
        {
            _endpoints.Remove(endpoint);
        }

        _logger.LogInformation("Stopped the retired loopback endpoint on port {Port}.", endpoint.Port);
    }

    private static bool IsGetOrHead(HttpListenerRequest request)
    {
        return IsMethod(request, "GET") || IsMethod(request, "HEAD");
    }

    private static bool IsMethod(HttpListenerRequest request, string method)
    {
        return string.Equals(request.HttpMethod, method, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLowercaseSha256(string value)
    {
        return value.Length == 64
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private sealed record ServerEndpointDto(
        [property: JsonPropertyName("overlayUrl")] string OverlayUrl);

    private sealed class ListenerEndpoint
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerContext, CancellationToken, Task> _handleContext;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly ConcurrentDictionary<Task, byte> _requests = new();
        private Task? _acceptLoop;
        private int _stopped;

        public ListenerEndpoint(
            int port,
            HostOptions options,
            Func<HttpListenerContext, CancellationToken, Task> handleContext,
            ILogger logger)
        {
            Port = port;
            _handleContext = handleContext;
            _logger = logger;
            _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
            _listener.IgnoreWriteExceptions = false;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.TimeoutManager.HeaderWait = options.RequestHeadersTimeout;
            _listener.TimeoutManager.IdleConnection = options.KeepAliveTimeout;
        }

        public int Port { get; }

        public void Start()
        {
            _listener.Start();
            _acceptLoop = AcceptLoopAsync();
        }

        public void CloseAfterFailedStart()
        {
            Interlocked.Exchange(ref _stopped, 1);
            _listener.Close();
            _shutdown.Dispose();
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            _shutdown.Cancel();
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }

            if (_acceptLoop is not null)
            {
                await _acceptLoop;
            }

            await Task.WhenAll(_requests.Keys);
            _listener.Close();
            _shutdown.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(_shutdown.Token);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    _logger.LogError(error, "The loopback HTTP accept loop failed on port {Port}.", Port);
                    break;
                }

                var request = _handleContext(context, _shutdown.Token);
                _requests.TryAdd(request, 0);
                _ = request.ContinueWith(
                    completed => _requests.TryRemove(completed, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}
