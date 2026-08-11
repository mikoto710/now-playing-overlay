using NowPlayingOverlay.Host.Media.Windows;

namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed record SpotifySessionSelection(
    SpotifySessionSelectionStatus Status,
    IMediaSessionAdapter? Session,
    int MatchCount)
{
    public static SpotifySessionSelection NotFound { get; } =
        new(SpotifySessionSelectionStatus.NotFound, null, 0);
}
