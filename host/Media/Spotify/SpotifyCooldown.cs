namespace NowPlayingOverlay.Host.Media.Spotify;

internal enum SpotifyEndpoint
{
    CurrentlyPlaying,
    Token,
}

internal sealed record SpotifyCooldown(
    string ClientKey,
    SpotifyEndpoint Endpoint,
    bool IsQuotaExceeded,
    DateTimeOffset FirstFailureAtUtc,
    DateTimeOffset NotBeforeUtc,
    int Consecutive429);

/// <summary>A real 429 or admission blocked by a previously recorded 429.</summary>
internal sealed class SpotifyRateLimitException(SpotifyCooldown cooldown, TimeSpan retryAfter)
    : Exception(cooldown.IsQuotaExceeded ? "Spotify API quota exceeded." : "Spotify request rate limited; reason unconfirmed.")
{
    public SpotifyCooldown Cooldown { get; } = cooldown;
    public TimeSpan RetryAfter { get; } = retryAfter;
}
