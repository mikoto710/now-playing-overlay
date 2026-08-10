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
        Assert.Equal(string.Empty, snapshot.SourceAppUserModelId);
        Assert.Null(snapshot.Track);
        Assert.Null(snapshot.Artwork);
        Assert.Null(snapshot.Identity);
        Assert.Equal(TimeSpan.Zero, snapshot.ObservedAt.Offset);
    }

    [Fact]
    public void CreatePlayingBuildsIdentityWithoutSupplementalMetadata()
    {
        var track = TrackMetadata.Create("Title", "Artist", "Album");

        var snapshot = CreateSnapshot(1, PlaybackState.Playing, track: track);

        Assert.Equal(TrackIdentity.Create("Spotify.exe", track), snapshot.Identity);
    }

    [Fact]
    public void VisibleStateIgnoresRevisionAndObservationTime()
    {
        var track = TrackMetadata.Create("Title", "Artist", "Album");
        var first = CreateSnapshot(1, PlaybackState.Playing, track: track);
        var second = NowPlayingSnapshot.Create(
            ServerInstanceId,
            99,
            "Spotify.exe",
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
            "Spotify.exe",
            PlaybackState.Playing,
            track,
            first.Artwork,
            first.ObservedAt);
        var differentArtwork = CreateSnapshot(2, PlaybackState.Playing, track, CreateArtwork(2));

        Assert.False(first.HasSameVisibleStateAs(differentServer));
        Assert.False(first.HasSameVisibleStateAs(differentArtwork));
    }

    [Fact]
    public void PausedAndStoppedAllowMissingTrack()
    {
        Assert.Null(CreateSnapshot(1, PlaybackState.Paused).Track);
        Assert.Null(CreateSnapshot(2, PlaybackState.Stopped).Track);
    }

    [Theory]
    [InlineData((int)PlaybackState.Playing, "", false)]
    [InlineData((int)PlaybackState.Playing, "Spotify.exe", false)]
    [InlineData((int)PlaybackState.Paused, "", false)]
    [InlineData((int)PlaybackState.Stopped, "", false)]
    [InlineData((int)PlaybackState.Idle, "", false)]
    [InlineData((int)PlaybackState.Idle, "Spotify.exe", true)]
    [InlineData((int)PlaybackState.Unavailable, "Spotify.exe", false)]
    public void CreateEnforcesPlaybackStateMatrix(
        int playbackValue,
        string source,
        bool includeTrack)
    {
        var playback = (PlaybackState)playbackValue;
        var track = includeTrack ? TrackMetadata.Create("Title", "Artist", null) : null;

        Assert.Throws<ArgumentException>(
            () => NowPlayingSnapshot.Create(
                ServerInstanceId,
                1,
                source,
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
                "Spotify.exe",
                PlaybackState.Paused,
                track: null,
                CreateArtwork(1),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateRejectsUnknownPlaybackState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NowPlayingSnapshot.Create(
                ServerInstanceId,
                1,
                "Spotify.exe",
                (PlaybackState)999,
                track: null,
                artwork: null,
                DateTimeOffset.UtcNow));
    }

    private static NowPlayingSnapshot CreateSnapshot(
        long revision,
        PlaybackState playback,
        TrackMetadata? track = null,
        ArtworkDescriptor? artwork = null)
    {
        return NowPlayingSnapshot.Create(
            ServerInstanceId,
            revision,
            "Spotify.exe",
            playback,
            track,
            artwork,
            DateTimeOffset.UtcNow);
    }

    private static ArtworkDescriptor CreateArtwork(long revision)
    {
        return ArtworkDescriptor.Create(
            revision,
            "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
            "image/png",
            1024);
    }
}
