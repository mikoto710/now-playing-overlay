using System.Net.Http.Headers;
using System.Text.Json;

namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed record SpotifyRateLimitResponse(bool IsQuotaExceeded, string ParseStatus, RetryConditionHeaderValue? RetryAfter)
{
    private const int MaximumBytes = 16 * 1024;

    public static async Task<SpotifyRateLimitResponse> ReadAsync(
        SpotifyHttpResponse response, CancellationToken cancellationToken)
    {
        var retryAfter = ReadRetryAfter(response.Message);
        try
        {
            var bytes = await response.ReadBodyAsync(MaximumBytes, cancellationToken);
            if (bytes.Length == 0)
            {
                return new(false, "empty", retryAfter);
            }

            using var document = JsonDocument.Parse(bytes);
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
        catch (InvalidDataException)
        {
            return new(false, "too_large", retryAfter);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            // Headers already established 429; a body failure must not discard its cooldown.
            return new(false, "read_failed", retryAfter);
        }
    }

    public TimeSpan? GetRetryDelay(DateTimeOffset now)
    {
        var delay = RetryAfter?.Delta ?? (RetryAfter?.Date is { } date ? date - now : null);
        return delay is { } value && value < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static RetryConditionHeaderValue? ReadRetryAfter(HttpResponseMessage response)
    {
        try
        {
            return response.Headers.RetryAfter;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
