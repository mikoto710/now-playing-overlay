using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Models;

public sealed class PlaybackTimelineTests
{
    [Fact]
    public void CreatePreservesAnchorAndNormalizesSampledAtToUtc()
    {
        var localTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.FromHours(8));

        var timeline = PlaybackTimeline.Create(10_000, 240_000, localTime);

        Assert.Equal(10_000, timeline.PositionMs);
        Assert.Equal(240_000, timeline.DurationMs);
        Assert.Equal(localTime.ToUniversalTime(), timeline.SampledAt);
        Assert.Equal(TimeSpan.Zero, timeline.SampledAt.Offset);
    }

    [Fact]
    public void CreateAllowsPositionAtDurationBoundary()
    {
        var timeline = PlaybackTimeline.Create(240_000, 240_000, DateTimeOffset.UtcNow);

        Assert.Equal(timeline.DurationMs, timeline.PositionMs);
    }

    [Theory]
    [InlineData(-1, 240_000, "positionMs")]
    [InlineData(0, 0, "durationMs")]
    [InlineData(240_001, 240_000, "positionMs")]
    public void CreateRejectsInvalidAnchor(
        long positionMs,
        long durationMs,
        string parameterName)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => PlaybackTimeline.Create(positionMs, durationMs, DateTimeOffset.UtcNow));

        Assert.Equal(parameterName, error.ParamName);
    }
}
