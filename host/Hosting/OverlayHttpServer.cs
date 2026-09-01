using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Owns the loopback endpoint lifecycle and transactional port rebind. A rebind starts the
/// candidate listener before invoking persistence; only a successful save makes it authoritative.
/// The previous endpoint remains tracked until its grace-period retirement completes.
/// </summary>
internal sealed class OverlayHttpServer : IOverlayHttpRuntime
{
    private readonly object _gate = new();
    private readonly HostOptions _options;
    private readonly ServerEndpointChangeBroadcaster _endpointChanges;
    private readonly ExternalIngestHttpHandler? _externalIngestHandler;
    private readonly OverlayHttpRequestDispatcher _dispatcher;
    private readonly ILogger<OverlayHttpServer> _logger;
    private readonly SemaphoreSlim _rebindGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<LoopbackListenerEndpoint> _endpoints = [];
    private readonly HashSet<Task> _retirements = [];
    private LoopbackListenerEndpoint? _currentEndpoint;
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
        _endpointChanges = endpointChanges ?? throw new ArgumentNullException(nameof(endpointChanges));
        _externalIngestHandler = externalIngestHandler;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var sse = new SseConnectionHandler(
            options.SseHeartbeatInterval,
            store,
            sseLimiter,
            endpointChanges);
        var endpoints = new OverlayEndpointHandlers(
            pageAsset,
            browserProducerAsset
                ?? BrowserProducerAsset.LoadEmbedded(typeof(BrowserProducerAsset).Assembly),
            store,
            artworkCache,
            healthService,
            appearanceState,
            sse,
            spotifyCallbackBroker,
            externalIngestHandler);
        _dispatcher = new OverlayHttpRequestDispatcher(
            options,
            requestLimiter,
            endpoints,
            logger);
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
            LoopbackListenerEndpoint previous;
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
            var candidateStarted = false;
            var candidateCommitted = false;
            try
            {
                candidate.Start();
                candidateStarted = true;
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

                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _endpoints.Add(candidate);
                    _currentEndpoint = candidate;
                    candidateCommitted = true;
                }

                var overlayUrl = OverlayEndpoint.BuildUrl(newPort);
                _endpointChanges.Publish(overlayUrl);
                _logger.LogInformation(
                    "Moved the loopback endpoint from port {OldPort} to {NewPort}; the old endpoint remains during the migration grace period.",
                    previous.Port,
                    newPort);
                TrackRetirement(RetireEndpointAsync(previous, _options.PortRebindGracePeriod));
            }
            finally
            {
                if (candidateStarted && !candidateCommitted)
                {
                    await candidate.StopAsync();
                }
            }
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
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            _rebindGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _rebindGate.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            await StopCoreAsync(CancellationToken.None);
            _externalIngestHandler?.Dispose();
            _shutdown.Dispose();
        }
        finally
        {
            _rebindGate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        LoopbackListenerEndpoint[] endpoints;
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

    private LoopbackListenerEndpoint CreateEndpoint(int port)
    {
        return new LoopbackListenerEndpoint(port, _options, _dispatcher.HandleAsync, _logger);
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

    private async Task RetireEndpointAsync(
        LoopbackListenerEndpoint endpoint,
        TimeSpan gracePeriod)
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

}
