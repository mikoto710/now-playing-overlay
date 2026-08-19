using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Models;

public sealed class NowPlayingSnapshotTests
{
    private static readonly Guid ServerInstanceId = Guid.Parse("5f5d7c68-e115-4d2d-89f4-2371ace843df");

    [Fact]
    public void CreateInitialBuildsUnavailableRevisionZeroSnapshot()
    {
        var localTime = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));

        var snapshot = NowPlayingSnapshot.CreateInitial(ServerInstanceId, localTime);

        Assert.Equal(ServerInstanceId, snapshot.ServerInstanceId);
        Assert.Equal(0, snapshot.SnapshotRevision);
        Assert.Equal(PlaybackState.Unavailable, snapshot.Playback);
        Assert.Null(snapshot.Source);
        Assert.Null(snapshot.Track);
        Assert.Null(snapshot.Timeline);
        Assert.Null(snapshot.Artwork);
        Assert.Null(snapshot.Identity);
        Assert.Equal(TimeSpan.Zero, snapshot.ObservedAt.Offset);
    }

    [Fact]
    public void CreatePlayingBuildsIdentityWithoutSupplementalMetadata()
    {
        var track = TrackMetadata.Create("Title", "Artist", "Album");

        var snapshot = CreateSnapshot(1, PlaybackState.Playing, track: track);

        Assert.Equal(
            new TrackIdentity(SourceKey.WindowsMedia("Player.App"), "Title", "Artist"),
            snapshot.Identity);
    }

    [Fact]
    public void VisibleStateIgnoresRevisionAndObservationTime()
    {
        var track = TrackMetadata.Create("Title", "Artist", "Album");
        var first = CreateSnapshot(1, PlaybackState.Playing, track: track);
        var second = NowPlayingSnapshot.Create(
            ServerInstanceId,
            99,
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            track,
            artwork: null,
            first.ObservedAt.AddMinutes(1));

        Assert.True(first.HasSameVisibleStateAs(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void VisibleStateUsesTrackMetadataContentEquality()
    {
        var firstTrack = TrackMetadata.Create("Title", "Artist", "Album", genres: ["Rock"]);
        var equivalentTrack = TrackMetadata.Create("Title", "Artist", "Album", genres: ["Rock"]);
        var changedTrack = TrackMetadata.Create("Title", "Artist", "Album", genres: ["Pop"]);
        var first = CreateSnapshot(1, PlaybackState.Playing, track: firstTrack);

        Assert.True(
            first.HasSameVisibleStateAs(
                CreateSnapshot(2, PlaybackState.Playing, track: equivalentTrack)));
        Assert.False(
            first.HasSameVisibleStateAs(
                CreateSnapshot(2, PlaybackState.Playing, track: changedTrack)));
    }

    [Fact]
    public void VisibleStateIncludesServerInstanceAndArtworkRevision()
    {
        var track = TrackMetadata.Create("Title", "Artist", "Album");
        var first = CreateSnapshot(1, PlaybackState.Playing, track, CreateArtwork(1));
        var differentServer = NowPlayingSnapshot.Create(
            Guid.NewGuid(),
            1,
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            track,
            first.Artwork,
            first.ObservedAt);
        var differentArtwork = CreateSnapshot(2, PlaybackState.Playing, track, CreateArtwork(2));

        Assert.False(first.HasSameVisibleStateAs(differentServer));
        Assert.False(first.HasSameVisibleStateAs(differentArtwork));
    }

    [Fact]
    public void VisibleStateIncludesTimelineNullTransitions()
    {
        var track = TrackMetadata.Create("Title", "Artist", null);
        var withoutTimeline = CreateSnapshot(1, PlaybackState.Playing, track: track);
        var withTimeline = CreateSnapshot(
            2,
            PlaybackState.Playing,
            track: track,
            timeline: PlaybackTimeline.Create(10_000, 240_000, DateTimeOffset.UtcNow));

        Assert.False(withoutTimeline.HasSameVisibleStateAs(withTimeline));
        Assert.False(withTimeline.HasSameVisibleStateAs(withoutTimeline));
    }

    [Fact]
    public void PausedTimelineIgnoresResamplingButIncludesPositionAndDuration()
    {
        var sampledAt = new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);
        var track = TrackMetadata.Create("Title", "Artist", null);
        var first = CreateSnapshot(
            1,
            PlaybackState.Paused,
            track: track,
            timeline: PlaybackTimeline.Create(10_000, 240_000, sampledAt));
        var resampled = CreateSnapshot(
            2,
            PlaybackState.Paused,
            track: track,
            timeline: PlaybackTimeline.Create(10_000, 240_000, sampledAt.AddMinutes(1)));
        var moved = CreateSnapshot(
            2,
            PlaybackState.Paused,
            track: track,
            timeline: PlaybackTimeline.Create(10_001, 240_000, sampledAt.AddMinutes(1)));
        var durationChanged = CreateSnapshot(
            2,
            PlaybackState.Paused,
            track: track,
            timeline: PlaybackTimeline.Create(10_000, 240_001, sampledAt.AddMinutes(1)));

        Assert.True(first.HasSameVisibleStateAs(resampled));
        Assert.False(first.HasSameVisibleStateAs(moved));
        Assert.False(first.HasSameVisibleStateAs(durationChanged));
    }

    [Fact]
    public void PlayingTimelineProjectsOldAnchorUsingInternalTolerance()
    {
        var sampledAt = new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);
        var track = TrackMetadata.Create("Title", "Artist", null);
        var first = CreateSnapshot(
            1,
            PlaybackState.Playing,
            track: track,
            timeline: PlaybackTimeline.Create(10_000, 240_000, sampledAt));
        var atTolerance = CreateSnapshot(
            2,
            PlaybackState.Playing,
            track: track,
            timeline: PlaybackTimeline.Create(12_500, 240_000, sampledAt.AddSeconds(2)));
        var beyondTolerance = CreateSnapshot(
            2,
            PlaybackState.Playing,
            track: track,
            timeline: PlaybackTimeline.Create(12_501, 240_000, sampledAt.AddSeconds(2)));
        var durationChanged = CreateSnapshot(
            2,
            PlaybackState.Playing,
            track: track,
            timeline: PlaybackTimeline.Create(12_000, 240_001, sampledAt.AddSeconds(2)));

        Assert.True(first.HasSameVisibleStateAs(atTolerance));
        Assert.False(first.HasSameVisibleStateAs(beyondTolerance));
        Assert.False(first.HasSameVisibleStateAs(durationChanged));
    }

    [Fact]
    public void PausedAndStoppedAllowMissingTrack()
    {
        Assert.Null(CreateSnapshot(1, PlaybackState.Paused).Track);
        Assert.Null(CreateSnapshot(2, PlaybackState.Stopped).Track);
    }

    [Theory]
    [InlineData((int)PlaybackState.Playing, false, false)]
    [InlineData((int)PlaybackState.Playing, true, false)]
    [InlineData((int)PlaybackState.Paused, false, false)]
    [InlineData((int)PlaybackState.Stopped, false, false)]
    [InlineData((int)PlaybackState.Idle, false, false)]
    [InlineData((int)PlaybackState.Idle, true, true)]
    [InlineData((int)PlaybackState.Unavailable, true, true)]
    public void CreateEnforcesPlaybackStateMatrix(
        int playbackValue,
        bool includeSource,
        bool includeTrack)
    {
        var playback = (PlaybackState)playbackValue;
        var track = includeTrack ? TrackMetadata.Create("Title", "Artist", null) : null;

        Assert.Throws<ArgumentException>(
            () => NowPlayingSnapshot.Create(
                ServerInstanceId,
                1,
                includeSource ? SourceDescriptor.WindowsMedia("Player.App") : null,
                playback,
                track,
                artwork: null,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateRejectsArtworkWithoutTrack()
    {
        Assert.Throws<ArgumentException>(
            () => NowPlayingSnapshot.Create(
                ServerInstanceId,
                1,
                SourceDescriptor.WindowsMedia("Player.App"),
                PlaybackState.Paused,
                track: null,
                CreateArtwork(1),
                DateTimeOffset.UtcNow));
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
            () => NowPlayingSnapshot.Create(
                ServerInstanceId,
                1,
                source,
                playback,
                track: null,
                PlaybackTimeline.Create(10_000, 240_000, DateTimeOffset.UtcNow),
                artwork: null,
                DateTimeOffset.UtcNow));

        Assert.Equal("timeline", error.ParamName);
    }

    private static NowPlayingSnapshot CreateSnapshot(
        long revision,
        PlaybackState playback,
        TrackMetadata? track = null,
        ArtworkDescriptor? artwork = null,
        PlaybackTimeline? timeline = null)
    {
        return NowPlayingSnapshot.Create(
            ServerInstanceId,
            revision,
            SourceDescriptor.WindowsMedia("Player.App"),
            playback,
            track,
            timeline,
            artwork,
            DateTimeOffset.UtcNow);
    }

    private static ArtworkDescriptor CreateArtwork(long revision)
    {
        return new ArtworkDescriptor(
            revision,
            "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
            "image/png",
            1024);
    }
}
