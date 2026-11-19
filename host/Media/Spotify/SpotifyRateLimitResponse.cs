using System.Text.Json;

namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed record SpotifyRateLimitResponse(bool IsQuotaExceeded, string ParseStatus, TimeSpan? RetryAfter)
{
    private const int MaximumBytes = 16 * 1024;

    public static async Task<SpotifyRateLimitResponse> ReadAsync(
        HttpResponseMessage response, TimeProvider clock, CancellationToken cancellationToken)
    {
        var retryAfter = GetRetryAfter(response, clock);
        if (response.Content.Headers.ContentLength is > MaximumBytes)
        {
            return new(false, "too_large", retryAfter);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = new byte[MaximumBytes + 1];
            var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, timeout.Token);
            if (count > MaximumBytes)
            {
                return new(false, "too_large", retryAfter);
            }

            if (count == 0)
            {
                return new(false, "empty", retryAfter);
            }

            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("reason", out var reason)
                || reason.ValueKind != JsonValueKind.String)
            {
                return new(false, "missing_reason", retryAfter);
            }

            var quota = reason.ValueEquals("QUOTA_EXCEEDED");
            return new(quota, quota ? "ok" : "unknown_reason", retryAfter);
        }
        catch (JsonException)
        {
            return new(false, "invalid_json", retryAfter);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException)
        {
            // Headers already established 429; a body failure must not discard its cooldown.
            return new(false, "read_failed", retryAfter);
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response, TimeProvider clock)
    {
        try
        {
            var header = response.Headers.RetryAfter;
            var delay = header?.Delta ?? (header?.Date is { } date ? date - clock.GetUtcNow() : null);
            return delay is { } value && value < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
