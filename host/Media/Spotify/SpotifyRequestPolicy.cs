using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Media.Spotify;

/// <summary>
/// Admits actual HTTP sends, records bounded diagnostics, and retains independent endpoint
/// cooldowns across source changes, reauthorization, and process restarts.
/// </summary>
internal sealed class SpotifyRequestPolicy : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<(string, SpotifyEndpoint), ActiveCooldown> _cooldowns = [];
    private readonly ConcurrentDictionary<(string, SpotifyEndpoint), SemaphoreSlim> _requests = [];
    private readonly Dictionary<SpotifyEndpoint, RequestCounts> _counts = [];
    private readonly TimeProvider _clock;
    private readonly Func<double> _jitter;
    private readonly SpotifyCooldownStore? _store;
    private readonly ILogger<SpotifyRequestPolicy> _logger;
    private readonly ITimer? _summaryTimer;
    private string _pollingState = "unselected";
    private double? _pollIntervalSeconds;
    private bool _disposed;

    public SpotifyRequestPolicy(string? path = null, TimeProvider? timeProvider = null,
        Func<double>? jitter = null, ILogger<SpotifyRequestPolicy>? logger = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        _jitter = jitter ?? Random.Shared.NextDouble;
        _logger = logger ?? NullLogger<SpotifyRequestPolicy>.Instance;
        _store = path is null ? null : new SpotifyCooldownStore(path);
        try
        {
            foreach (var entry in _store?.Load() ?? [])
            {
                var duration = entry.NotBeforeUtc - _clock.GetUtcNow();
                _cooldowns[(entry.ClientKey, entry.Endpoint)] = new(entry, _clock.GetTimestamp(), duration);
                if (duration > TimeSpan.Zero)
                {
                    _logger.LogInformation(
                        "Spotify cooldown restored. Endpoint={Endpoint} Classification={Classification} NextAttemptAtUtc={NextAttemptAtUtc:O} RemainingSeconds={RemainingSeconds} Consecutive429={Consecutive429}.",
                        EndpointName(entry.Endpoint), entry.IsQuotaExceeded ? "quota" : "rate_or_unknown",
                        entry.NotBeforeUtc, duration.TotalSeconds, entry.Consecutive429);
                }
            }
        }
        catch (Exception error) when (IsStorageError(error))
        {
            _logger.LogWarning("Spotify cooldown load failed. ErrorType={ErrorType}.", error.GetType().Name);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _summaryTimer = _clock.CreateTimer(_ =>
            {
                lock (_gate) { WriteSummaryLocked(); }
            }, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        }
    }

    public SpotifyRateLimitException? GetCooldown(SpotifyClientId clientId, SpotifyEndpoint? endpoint = null)
    {
        var key = GetClientKey(clientId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ActiveCooldown? latest = null;
            var remaining = TimeSpan.Zero;
            foreach (var ((clientKey, target), cooldown) in _cooldowns)
            {
                if (clientKey == key && (endpoint is null || target == endpoint))
                {
                    var candidate = cooldown.Duration - _clock.GetElapsedTime(cooldown.StartedTimestamp);
                    if (candidate > remaining)
                    {
                        latest = cooldown;
                        remaining = candidate;
                    }
                }
            }

            return latest is null ? null : new(latest.Value, remaining);
        }
    }

    public async Task<SpotifyHttpResponse> SendAsync(SpotifyClientId clientId, SpotifyEndpoint endpoint,
        Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        var key = (GetClientKey(clientId), endpoint);
        var requestGate = _requests.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            if (GetCooldown(clientId, endpoint) is { } existing)
            {
                throw existing;
            }

            lock (_gate)
            {
                GetCounts(endpoint).Requests++;
            }
            var response = await send();
            if ((int)response.StatusCode == 429)
            {
                using (response)
                {
                    var details = await SpotifyRateLimitResponse.ReadAsync(response, _clock, cancellationToken);
                    throw Register429(key, details);
                }
            }

            var received = new SpotifyHttpResponse(response, _clock);
            lock (_gate)
            {
                if (response.IsSuccessStatusCode)
                {
                    GetCounts(endpoint).Successes++;
                    if (_cooldowns.Remove(key, out var recovered))
                    {
                        PersistLocked();
                        _logger.LogInformation(
                            "Spotify request recovered. Endpoint={Endpoint} Consecutive429={Consecutive429} LimitedSeconds={LimitedSeconds}.",
                            EndpointName(endpoint), recovered.Value.Consecutive429,
                            Math.Max(0, (_clock.GetUtcNow() - recovered.Value.FirstFailureAtUtc).TotalSeconds));
                    }
                }
            }

            return received;
        }
        finally
        {
            requestGate.Release();
        }
    }

    public void SetPollingState(string state, TimeSpan? interval = null)
    {
        lock (_gate)
        {
            if (_pollingState is "rate-cooldown" or "quota-cooldown"
                && state is not ("rate-cooldown" or "quota-cooldown")
                && interval is { } nextInterval)
            {
                _logger.LogInformation("Spotify polling resumed. PollingState={PollingState} IntervalSeconds={IntervalSeconds}.",
                    state, nextInterval.TotalSeconds);
            }

            _pollingState = state;
            _pollIntervalSeconds = interval?.TotalSeconds;
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; }
        _summaryTimer?.Dispose();
    }

    private SpotifyRateLimitException Register429((string Client, SpotifyEndpoint Endpoint) key,
        SpotifyRateLimitResponse details)
    {
        lock (_gate)
        {
            GetCounts(key.Endpoint).Responses429++;
            _cooldowns.TryGetValue(key, out var previous);
            var count = Math.Min(previous?.Value.Consecutive429 ?? 0, 1000) + 1;
            var quota = details.IsQuotaExceeded;
            var localSeconds = quota
                ? Math.Min(900 * Math.Pow(2, Math.Min(count - 1, 2)), 3600)
                : Math.Min(30 * Math.Pow(2, Math.Min(count - 1, 4)), 300);
            var random = _jitter();
            var jitterSeconds = localSeconds * 0.1 * (double.IsFinite(random) ? Math.Clamp(random, 0, 1) : 0);
            var seconds = Math.Max(details.RetryAfter?.TotalSeconds ?? 0, localSeconds) + jitterSeconds;
            var now = _clock.GetUtcNow();
            var maximumDuration = DateTimeOffset.MaxValue - now;
            var duration = seconds >= maximumDuration.TotalSeconds ? maximumDuration : TimeSpan.FromSeconds(seconds);
            var entry = new SpotifyCooldown(key.Client, key.Endpoint, quota,
                previous?.Value.FirstFailureAtUtc ?? now, now.Add(duration), count);
            _cooldowns[key] = new(entry, _clock.GetTimestamp(), duration);
            PersistLocked();
            _logger.LogWarning(
                "Spotify HTTP 429. Endpoint={Endpoint} Classification={Classification} Reason={Reason} RetryAfterSeconds={RetryAfterSeconds} ChosenDelaySeconds={ChosenDelaySeconds} NextAttemptAtUtc={NextAttemptAtUtc:O} Consecutive429={Consecutive429} BodyParseStatus={BodyParseStatus}.",
                EndpointName(key.Endpoint), quota ? "quota" : "rate_or_unknown",
                details.IsQuotaExceeded ? "QUOTA_EXCEEDED" : "unconfirmed",
                details.RetryAfter?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing",
                duration.TotalSeconds, entry.NotBeforeUtc, count, details.ParseStatus);
            return new(entry, duration);
        }
    }

    private void PersistLocked()
    {
        try { _store?.Save(_cooldowns.Values.Select(value => value.Value)); }
        catch (Exception error) when (IsStorageError(error))
        {
            _logger.LogWarning("Spotify cooldown persist failed; retaining in-memory cooldown. ErrorType={ErrorType}.", error.GetType().Name);
        }
    }

    private void WriteSummaryLocked()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (endpoint, counts) in _counts)
        {
            _logger.LogInformation(
                "Spotify request summary. Endpoint={Endpoint} Requests={Requests} Successes={Successes} Responses429={Responses429} PollingState={PollingState} IntervalSeconds={IntervalSeconds}.",
                EndpointName(endpoint), counts.Requests, counts.Successes, counts.Responses429, _pollingState, _pollIntervalSeconds);
            counts.Requests = 0;
            counts.Successes = 0;
            counts.Responses429 = 0;
        }
    }

    private RequestCounts GetCounts(SpotifyEndpoint endpoint)
    {
        if (!_counts.TryGetValue(endpoint, out var counts))
        {
            counts = new();
            _counts[endpoint] = counts;
        }

        return counts;
    }

    private static string EndpointName(SpotifyEndpoint endpoint) => endpoint == SpotifyEndpoint.Token ? "token" : "currently-playing";
    private static string GetClientKey(SpotifyClientId clientId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId.Value)));
    private static bool IsStorageError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    private sealed record ActiveCooldown(SpotifyCooldown Value, long StartedTimestamp, TimeSpan Duration);
    private sealed class RequestCounts
    {
        public long Requests;
        public long Successes;
        public long Responses429;
    }
}
