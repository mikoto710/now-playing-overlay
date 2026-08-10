namespace NowPlayingOverlay.Host.Media;

internal sealed record SpotifySessionSelection(
    SpotifySessionSelectionStatus Status,
    IMediaSessionAdapter? Session,
    int MatchCount)
{
    public static SpotifySessionSelection NotFound { get; } =
        new(SpotifySessionSelectionStatus.NotFound, null, 0);
}
