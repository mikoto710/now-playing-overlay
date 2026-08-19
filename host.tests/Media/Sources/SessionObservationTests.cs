using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.Sources;

public sealed class SessionObservationTests
{
    [Fact]
    public void CreatePreservesExactSourceKeyAndBuildsIdentity()
    {
        var track = TrackMetadata.Create("Title", "Artist", null);

        var observation = SessionObservation.Create(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            track,
            new StubArtworkReader());

        Assert.Equal("Player.App", observation.Source!.Key.InstanceId);
        Assert.Equal(
            new TrackIdentity(SourceKey.WindowsMedia("Player.App"), "Title", "Artist"),
            observation.Identity);
        Assert.Null(observation.Timeline);
    }

    [Fact]
    public void CreateAllowsUnavailableWithoutPlatformObjects()
    {
        var observation = SessionObservation.Create(null, PlaybackState.Unavailable);

        Assert.Null(observation.Source);
        Assert.Null(observation.Track);
        Assert.Null(observation.Timeline);
        Assert.Null(observation.ArtworkReader);
    }

    [Theory]
    [InlineData((int)PlaybackState.Playing)]
    [InlineData((int)PlaybackState.Paused)]
    public void CreateAllowsTimelineForActivePlaybackStates(int playbackValue)
    {
        var timeline = PlaybackTimeline.Create(10_000, 240_000, DateTimeOffset.UtcNow);

        var observation = SessionObservation.Create(
            SourceDescriptor.WindowsMedia("Player.App"),
            (PlaybackState)playbackValue,
            TrackMetadata.Create("Title", "Artist", null),
            timeline: timeline);

        Assert.Same(timeline, observation.Timeline);
    }

    [Theory]
    [InlineData((int)PlaybackState.Stopped)]
    [InlineData((int)PlaybackState.Idle)]
    [InlineData((int)PlaybackState.Unavailable)]
    public void CreateRejectsTimelineOutsideActivePlaybackStates(int playbackValue)
    {
        var playback = (PlaybackState)playbackValue;
        var source = playback == PlaybackState.Unavailable
            ? null
            : SourceDescriptor.WindowsMedia("Player.App");

        var error = Assert.Throws<ArgumentException>(
            () => SessionObservation.Create(
                source,
                playback,
                timeline: PlaybackTimeline.Create(10_000, 240_000, DateTimeOffset.UtcNow)));

        Assert.Equal("timeline", error.ParamName);
    }

    [Fact]
    public void CreateRejectsArtworkWithoutTrack()
    {
        Assert.Throws<ArgumentException>(
            () => SessionObservation.Create(
                SourceDescriptor.WindowsMedia("Player.App"),
                PlaybackState.Paused,
                artworkReader: new StubArtworkReader()));
    }

    [Fact]
    public void ArtworkPayloadCopiesInputBytes()
    {
        byte[] bytes = [1, 2, 3];
        var payload = ArtworkPayload.Create(bytes);

        bytes[0] = 9;

        Assert.Equal([1, 2, 3], payload.Bytes.ToArray());
    }

    private sealed class StubArtworkReader : IArtworkReader
    {
        public ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<ArtworkPayload?>(null);
        }
    }
}
