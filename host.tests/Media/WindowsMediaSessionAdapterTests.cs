using Windows.Media;
using Windows.Media.Control;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media;

public sealed class WindowsMediaSessionAdapterTests
{
    [Fact]
    public void MapSnapshotCopiesCompleteGsmTcMetadata()
    {
        var artwork = new StubArtworkReader();
        var snapshot = new WindowsMediaSessionSnapshot
        {
            SourceAppUserModelId = "Spotify.exe",
            PlaybackStatus = MediaSessionPlaybackStatus.Playing,
            Title = "Title",
            Artist = "Artist",
            AlbumTitle = "Album",
            AlbumArtist = "Album Artist",
            Subtitle = "Subtitle",
            TrackNumber = 3,
            AlbumTrackCount = 12,
            PlaybackType = MediaPlaybackKind.Music,
            Genres = ["Rock", "Pop"],
            ArtworkReader = artwork,
        };

        var observation = WindowsMediaSessionAdapter.MapSnapshot(snapshot);

        Assert.Equal(PlaybackState.Playing, observation.Playback);
        Assert.Equal("Spotify.exe", observation.SourceAppUserModelId);
        Assert.NotNull(observation.Track);
        Assert.Equal("Title", observation.Track.Title);
        Assert.Equal("Artist", observation.Track.Artist);
        Assert.Equal("Album", observation.Track.AlbumTitle);
        Assert.Equal("Album Artist", observation.Track.AlbumArtist);
        Assert.Equal("Subtitle", observation.Track.Subtitle);
        Assert.Equal((uint)3, observation.Track.TrackNumber);
        Assert.Equal((uint)12, observation.Track.AlbumTrackCount);
        Assert.Equal(MediaPlaybackKind.Music, observation.Track.PlaybackType);
        Assert.Equal(["Rock", "Pop"], observation.Track.Genres);
        Assert.Same(artwork, observation.ArtworkReader);
    }

    [Theory]
    [InlineData((int)MediaSessionPlaybackStatus.Playing)]
    [InlineData((int)MediaSessionPlaybackStatus.Opened)]
    [InlineData((int)MediaSessionPlaybackStatus.Changing)]
    [InlineData((int)MediaSessionPlaybackStatus.Closed)]
    public void MapSnapshotUsesIdleWhenNoDisplayableTrackExists(int playbackStatusValue)
    {
        var playbackStatus = (MediaSessionPlaybackStatus)playbackStatusValue;
        var observation = WindowsMediaSessionAdapter.MapSnapshot(
            new WindowsMediaSessionSnapshot
            {
                SourceAppUserModelId = "Spotify.exe",
                PlaybackStatus = playbackStatus,
                Title = "  ",
                ArtworkReader = new StubArtworkReader(),
            });

        Assert.Equal(PlaybackState.Idle, observation.Playback);
        Assert.Null(observation.Track);
        Assert.Null(observation.ArtworkReader);
    }

    [Theory]
    [InlineData((int)MediaSessionPlaybackStatus.Paused, (int)PlaybackState.Paused)]
    [InlineData((int)MediaSessionPlaybackStatus.Stopped, (int)PlaybackState.Stopped)]
    public void MapSnapshotPreservesPausedAndStoppedMetadataWhenAvailable(
        int playbackStatusValue,
        int expectedValue)
    {
        var playbackStatus = (MediaSessionPlaybackStatus)playbackStatusValue;
        var expected = (PlaybackState)expectedValue;
        var observation = WindowsMediaSessionAdapter.MapSnapshot(
            new WindowsMediaSessionSnapshot
            {
                SourceAppUserModelId = "Spotify.exe",
                PlaybackStatus = playbackStatus,
                Title = "Track",
                TrackNumber = -1,
                AlbumTrackCount = 0,
            });

        Assert.Equal(expected, observation.Playback);
        Assert.Equal("Track", observation.Track!.Title);
        Assert.Null(observation.Track.TrackNumber);
        Assert.Null(observation.Track.AlbumTrackCount);
    }

    [Theory]
    [InlineData((int)GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed, (int)MediaSessionPlaybackStatus.Closed)]
    [InlineData((int)GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened, (int)MediaSessionPlaybackStatus.Opened)]
    [InlineData((int)GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing, (int)MediaSessionPlaybackStatus.Changing)]
    [InlineData((int)GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped, (int)MediaSessionPlaybackStatus.Stopped)]
    [InlineData((int)GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, (int)MediaSessionPlaybackStatus.Playing)]
    [InlineData((int)GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused, (int)MediaSessionPlaybackStatus.Paused)]
    public void MapsEveryWindowsPlaybackStatus(
        int inputValue,
        int expectedValue)
    {
        var input = (GlobalSystemMediaTransportControlsSessionPlaybackStatus)inputValue;
        var expected = (MediaSessionPlaybackStatus)expectedValue;
        Assert.Equal(expected, WindowsMediaSessionAdapter.MapPlaybackStatus(input));
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData((int)MediaPlaybackType.Unknown, (int)MediaPlaybackKind.Unknown)]
    [InlineData((int)MediaPlaybackType.Music, (int)MediaPlaybackKind.Music)]
    [InlineData((int)MediaPlaybackType.Video, (int)MediaPlaybackKind.Video)]
    [InlineData((int)MediaPlaybackType.Image, (int)MediaPlaybackKind.Image)]
    public void MapsEveryWindowsMediaPlaybackType(int inputValue, int expectedValue)
    {
        MediaPlaybackType? input = inputValue < 0 ? null : (MediaPlaybackType)inputValue;
        MediaPlaybackKind? expected = expectedValue < 0 ? null : (MediaPlaybackKind)expectedValue;
        Assert.Equal(expected, WindowsMediaSessionAdapter.MapPlaybackType(input));
    }

    private sealed class StubArtworkReader : IArtworkReader
    {
        public ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<ArtworkPayload?>(null);
        }
    }
}
