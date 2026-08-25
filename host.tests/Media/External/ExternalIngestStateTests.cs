using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.External;

public sealed class ExternalIngestStateTests
{
    private static readonly Guid ProducerId = Guid.Parse("df3c450a-36b0-46ee-b708-879a5cbf2b08");

    [Fact]
    public void CreateNormalizesTheBoundedTrackFields()
    {
        var state = ExternalIngestState.Create(
            ProducerId,
            7,
            PlaybackState.Playing,
            " Cafe\u0301\r\n",
            " Artist\u2028Name ",
            " Album ",
            " Track-42 ");

        Assert.Equal(ProducerId, state.ProducerId);
        Assert.Equal(7, state.ProducerRevision);
        Assert.Equal(PlaybackState.Playing, state.Playback);
        Assert.Equal("Café", state.Track!.Title);
        Assert.Equal("Artist Name", state.Track.Artist);
        Assert.Equal("Album", state.Track.AlbumTitle);
        Assert.Equal("Track-42", state.Track.ProviderTrackId);
    }

    [Theory]
    [InlineData((int)PlaybackState.Paused)]
    [InlineData((int)PlaybackState.Stopped)]
    [InlineData((int)PlaybackState.Idle)]
    public void CreateAllowsNonPlayingStatesWithoutTrack(int playbackValue)
    {
        var state = ExternalIngestState.Create(
            ProducerId,
            1,
            (PlaybackState)playbackValue);

        Assert.Null(state.Track);
    }

    [Fact]
    public void CreateRejectsPlayingWithoutTrack()
    {
        var error = Assert.Throws<ArgumentException>(
            () => ExternalIngestState.Create(
                ProducerId,
                1,
                PlaybackState.Playing));

        Assert.Equal("title", error.ParamName);
    }

    [Fact]
    public void CreateRejectsIdleWithTrack()
    {
        var error = Assert.Throws<ArgumentException>(
            () => ExternalIngestState.Create(
                ProducerId,
                1,
                PlaybackState.Idle,
                "Track"));

        Assert.Equal("title", error.ParamName);
    }

    [Fact]
    public void CreateRejectsPartialTrackWithoutTitle()
    {
        Assert.Throws<ArgumentException>(
            () => ExternalIngestState.Create(
                ProducerId,
                1,
                PlaybackState.Paused,
                artist: "Artist"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateRejectsNonPositiveRevision(long revision)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalIngestState.Create(
                ProducerId,
                revision,
                PlaybackState.Idle));
    }

    [Fact]
    public void CreateRejectsEmptyProducerAndHostOwnedUnavailable()
    {
        Assert.Throws<ArgumentException>(
            () => ExternalIngestState.Create(
                Guid.Empty,
                1,
                PlaybackState.Idle));
        Assert.Throws<ArgumentException>(
            () => ExternalIngestState.Create(
                ProducerId,
                1,
                PlaybackState.Unavailable));
    }

    [Fact]
    public void CreateRejectsUnknownPlaybackState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalIngestState.Create(
                ProducerId,
                1,
                (PlaybackState)999));
    }
}
