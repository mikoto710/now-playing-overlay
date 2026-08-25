using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed record ExternalIngestLimits
{
    public int MaximumBodyBytes { get; init; } = 16 * 1024;

    public TimeSpan BodyReadTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaximumRequestsPerWindow { get; init; } = 20;

    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromSeconds(1);

    public void Validate()
    {
        if (MaximumBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumBodyBytes));
        }

        if (BodyReadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BodyReadTimeout));
        }

        if (MaximumRequestsPerWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRequestsPerWindow));
        }

        if (RateLimitWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RateLimitWindow));
        }
    }
}

internal sealed class ExternalIngestHttpHandler : IDisposable
{
    public const string StatePath = "/ingest/v1/state";
    public const string HeartbeatPath = "/ingest/v1/heartbeat";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly object _keyGate = new();
    private IngestKey? _key;
    private long _keyGeneration;
    private readonly ExternalProducerLease _lease;
    private readonly ExternalIngestLimits _limits;
    private readonly FixedWindowRateLimiter _rateLimiter;
    private int _disposed;

    public ExternalIngestHttpHandler(
        IngestKey key,
        ExternalProducerLease lease,
        ExternalIngestLimits? limits = null,
        TimeProvider? timeProvider = null)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _limits = limits ?? new ExternalIngestLimits();
        _limits.Validate();
        _rateLimiter = new FixedWindowRateLimiter(
            _limits.MaximumRequestsPerWindow,
            _limits.RateLimitWindow,
            timeProvider ?? TimeProvider.System);
    }

    public string ExportKey()
    {
        lock (_keyGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return GetKeyLocked().Export();
        }
    }

    public void ReplaceKey(IngestKey replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        IngestKey previous;
        lock (_keyGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            previous = GetKeyLocked();
            _key = replacement;
            _keyGeneration = checked(_keyGeneration + 1);
            // Serialize revocation with final request authorization so an in-flight old-key
            // request cannot recreate the lease after rotation.
            _lease.Revoke();
        }

        previous.Dispose();
    }

    public async Task HandleAsync(
        HttpListenerContext context,
        bool heartbeat,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var request = context.Request;
        var response = context.Response;
        response.Headers[HttpResponseHeader.CacheControl] = "no-store";
        if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 405;
            response.Headers[HttpResponseHeader.Allow] = "POST";
            response.ContentLength64 = 0;
            return;
        }

        if (!_rateLimiter.TryAcquire(out var retryAfterSeconds))
        {
            response.StatusCode = 429;
            response.Headers[HttpResponseHeader.RetryAfter] = retryAfterSeconds.ToString();
            response.ContentLength64 = 0;
            return;
        }

        if (!TryAuthorize(request.Headers["Authorization"], out var keyGeneration))
        {
            WriteUnauthorized(response);
            return;
        }

        if (!HasJsonContentType(request)
            || !string.IsNullOrWhiteSpace(request.Headers["Content-Encoding"]))
        {
            response.StatusCode = 415;
            response.ContentLength64 = 0;
            return;
        }

        if (request.ContentLength64 > _limits.MaximumBodyBytes)
        {
            response.StatusCode = 413;
            response.ContentLength64 = 0;
            return;
        }

        byte[] body;
        try
        {
            body = await ReadBodyAsync(request, cancellationToken);
        }
        catch (RequestBodyTooLargeException)
        {
            response.StatusCode = 413;
            response.ContentLength64 = 0;
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            response.StatusCode = 408;
            response.ContentLength64 = 0;
            return;
        }

        try
        {
            lock (_keyGate)
            {
                if (keyGeneration != _keyGeneration)
                {
                    WriteUnauthorized(response);
                    return;
                }

                response.StatusCode = heartbeat
                    ? HandleHeartbeat(body)
                    : HandleState(body);
            }
        }
        catch (Exception error) when (error is JsonException
            or ArgumentException
            or InvalidDataException)
        {
            response.StatusCode = 400;
        }

        response.ContentLength64 = 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            IngestKey? key;
            lock (_keyGate)
            {
                key = _key;
                _key = null;
            }

            key?.Dispose();
        }
    }

    private bool TryAuthorize(string? authorization, out long keyGeneration)
    {
        lock (_keyGate)
        {
            var matches = GetKeyLocked().MatchesAuthorization(authorization);
            keyGeneration = _keyGeneration;
            return matches;
        }
    }

    private static void WriteUnauthorized(HttpListenerResponse response)
    {
        response.StatusCode = 401;
        response.Headers[HttpResponseHeader.WwwAuthenticate] = "Bearer";
        response.ContentLength64 = 0;
    }

    private IngestKey GetKeyLocked()
    {
        return _key ?? throw new ObjectDisposedException(nameof(ExternalIngestHttpHandler));
    }

    private int HandleState(ReadOnlySpan<byte> body)
    {
        var request = JsonSerializer.Deserialize<StateRequest>(body, JsonOptions)
            ?? throw new InvalidDataException("The ingest state body must not be null.");
        if (request.Track is not null && request.Track.Title is null)
        {
            throw new InvalidDataException("Track title must not be null.");
        }

        var state = ExternalIngestState.Create(
            request.ProducerId,
            request.ProducerRevision,
            request.Playback,
            request.Track?.Title,
            request.Track?.Artist,
            request.Track?.AlbumTitle,
            request.Track?.TrackId);
        return _lease.ApplyState(state) == ExternalLeaseStateResult.Accepted ? 204 : 409;
    }

    private int HandleHeartbeat(ReadOnlySpan<byte> body)
    {
        var request = JsonSerializer.Deserialize<HeartbeatRequest>(body, JsonOptions)
            ?? throw new InvalidDataException("The heartbeat body must not be null.");
        return _lease.Heartbeat(request.ProducerId) == ExternalLeaseHeartbeatResult.Renewed
            ? 204
            : 409;
    }

    private async Task<byte[]> ReadBodyAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_limits.BodyReadTimeout);
        using var body = request.ContentLength64 is > 0 and <= int.MaxValue
            ? new MemoryStream((int)request.ContentLength64)
            : new MemoryStream();
        var buffer = new byte[Math.Min(4096, _limits.MaximumBodyBytes)];
        while (true)
        {
            var read = await request.InputStream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                return body.ToArray();
            }

            if (body.Length + read > _limits.MaximumBodyBytes)
            {
                throw new RequestBodyTooLargeException();
            }

            body.Write(buffer, 0, read);
        }
    }

    private static bool HasJsonContentType(HttpListenerRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Parameters.Count > 1)
        {
            return false;
        }

        foreach (var parameter in contentType.Parameters)
        {
            if (!string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return contentType.CharSet is null
            || string.Equals(contentType.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowDuplicateProperties = false,
            MaxDepth = 8,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter<PlaybackState>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private sealed record StateRequest
    {
        [JsonPropertyName("producerId")]
        public required Guid ProducerId { get; init; }

        [JsonPropertyName("producerRevision")]
        public required long ProducerRevision { get; init; }

        [JsonPropertyName("playback")]
        public required PlaybackState Playback { get; init; }

        [JsonPropertyName("track")]
        public ExternalTrackRequest? Track { get; init; }
    }

    private sealed record ExternalTrackRequest
    {
        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("artist")]
        public string? Artist { get; init; }

        [JsonPropertyName("albumTitle")]
        public string? AlbumTitle { get; init; }

        [JsonPropertyName("trackId")]
        public string? TrackId { get; init; }
    }

    private sealed record HeartbeatRequest
    {
        [JsonPropertyName("producerId")]
        public required Guid ProducerId { get; init; }
    }

    private sealed class FixedWindowRateLimiter(
        int maximumRequests,
        TimeSpan window,
        TimeProvider timeProvider)
    {
        private readonly object _gate = new();
        private long _windowStarted = timeProvider.GetTimestamp();
        private int _requestCount;

        public bool TryAcquire(out int retryAfterSeconds)
        {
            lock (_gate)
            {
                var now = timeProvider.GetTimestamp();
                var elapsed = timeProvider.GetElapsedTime(_windowStarted, now);
                if (elapsed >= window)
                {
                    _windowStarted = now;
                    _requestCount = 0;
                    elapsed = TimeSpan.Zero;
                }

                if (_requestCount >= maximumRequests)
                {
                    retryAfterSeconds = Math.Max(
                        1,
                        (int)Math.Ceiling((window - elapsed).TotalSeconds));
                    return false;
                }

                _requestCount++;
                retryAfterSeconds = 0;
                return true;
            }
        }
    }

    private sealed class RequestBodyTooLargeException : Exception;
}
