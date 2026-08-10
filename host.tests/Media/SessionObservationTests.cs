using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media;

public sealed class SessionObservationTests
{
    [Fact]
    public void CreateNormalizesSourceAndBuildsIdentity()
    {
        var track = TrackMetadata.Create("Title", "Artist", null);

        var observation = SessionObservation.Create(
            " Spotify.exe ",
            PlaybackState.Playing,
            track,
            new StubArtworkReader());

        Assert.Equal("Spotify.exe", observation.SourceAppUserModelId);
        Assert.Equal(TrackIdentity.Create("Spotify.exe", track), observation.Identity);
    }

    [Fact]
    public void CreateAllowsUnavailableWithoutPlatformObjects()
    {
        var observation = SessionObservation.Create(null, PlaybackState.Unavailable);

        Assert.Equal(string.Empty, observation.SourceAppUserModelId);
        Assert.Null(observation.Track);
        Assert.Null(observation.ArtworkReader);
    }

    [Fact]
    public void CreateRejectsArtworkWithoutTrack()
    {
        Assert.Throws<ArgumentException>(
            () => SessionObservation.Create(
                "Spotify.exe",
                PlaybackState.Paused,
                artworkReader: new StubArtworkReader()));
    }

    [Fact]
    public void ArtworkPayloadCopiesInputBytes()
    {
        byte[] bytes = [1, 2, 3];
        var payload = ArtworkPayload.Create(bytes, " image/png ");

        bytes[0] = 9;

        Assert.Equal([1, 2, 3], payload.Bytes.ToArray());
        Assert.Equal("image/png", payload.DeclaredContentType);
    }

    private sealed class StubArtworkReader : IArtworkReader
    {
        public ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<ArtworkPayload?>(null);
        }
    }
}
