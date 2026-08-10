using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Hosting;

internal static class FakeScenario
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static IReadOnlyList<FakeSessionStep> Create()
    {
        var first = TrackMetadata.Create(
            "Fake Track A",
            "Protocol Artist",
            "Vertical Slice",
            playbackType: MediaPlaybackKind.Music,
            genres: ["Diagnostic"]);
        var second = TrackMetadata.Create(
            "Fake Track B",
            "Protocol Artist",
            "Vertical Slice",
            playbackType: MediaPlaybackKind.Music,
            genres: ["Diagnostic"]);
        var artwork = ArtworkPayload.Create(OnePixelPng, "image/png");

        return
        [
            new(TimeSpan.FromSeconds(1), Playing(first, new DelayedArtworkReader(artwork, TimeSpan.FromSeconds(1)))),
            new(TimeSpan.FromSeconds(4), SessionObservation.Create("Spotify.exe", PlaybackState.Paused, first)),
            new(TimeSpan.FromSeconds(2), Playing(first)),
            // Rapid A -> B updates exercise the coordinator's latest-wins path.
            new(TimeSpan.FromSeconds(2), Playing(second, new DelayedArtworkReader(artwork, TimeSpan.FromMilliseconds(750)))),
            new(TimeSpan.FromMilliseconds(25), Playing(first)),
            new(TimeSpan.FromMilliseconds(25), Playing(second, new DelayedArtworkReader(artwork, TimeSpan.FromMilliseconds(750)))),
            new(TimeSpan.FromSeconds(5), SessionObservation.Create(null, PlaybackState.Unavailable)),
        ];
    }

    private static SessionObservation Playing(TrackMetadata track, IArtworkReader? artwork = null)
    {
        return SessionObservation.Create("Spotify.exe", PlaybackState.Playing, track, artwork);
    }
}
