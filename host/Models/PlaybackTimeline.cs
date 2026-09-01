namespace NowPlayingOverlay.Host.Models;

/// <summary>
/// Validated playback position, duration, and sampling timestamp.
/// </summary>
internal sealed record PlaybackTimeline
{
    private PlaybackTimeline(
        long positionMs,
        long durationMs,
        DateTimeOffset sampledAt)
    {
        PositionMs = positionMs;
        DurationMs = durationMs;
        SampledAt = sampledAt;
    }

    public long PositionMs { get; }

    public long DurationMs { get; }

    public DateTimeOffset SampledAt { get; }

    public static PlaybackTimeline Create(
        long positionMs,
        long durationMs,
        DateTimeOffset sampledAt)
    {
        if (positionMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionMs),
                positionMs,
                "Timeline position must not be negative.");
        }

        if (durationMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMs),
                durationMs,
                "Timeline duration must be positive.");
        }

        if (positionMs > durationMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionMs),
                positionMs,
                "Timeline position must not exceed its duration.");
        }

        return new PlaybackTimeline(
            positionMs,
            durationMs,
            sampledAt.ToUniversalTime());
    }
}
