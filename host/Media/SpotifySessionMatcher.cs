namespace NowPlayingOverlay.Host.Media;

internal sealed class SpotifySessionMatcher
{
    public const string VerifiedSourceAppUserModelId = "Spotify.exe";

    public SpotifySessionSelection Select(IEnumerable<SpotifySessionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var matches = candidates
            .Where(candidate => string.Equals(
                candidate.SourceAppUserModelId,
                VerifiedSourceAppUserModelId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            return SpotifySessionSelection.NotFound;
        }

        if (matches.Length == 1)
        {
            return new SpotifySessionSelection(
                SpotifySessionSelectionStatus.Selected,
                matches[0].Session,
                matches.Length);
        }

        var playing = matches
            .Where(candidate => candidate.PlaybackStatus == MediaSessionPlaybackStatus.Playing)
            .ToArray();
        if (playing.Length == 1)
        {
            return new SpotifySessionSelection(
                SpotifySessionSelectionStatus.Selected,
                playing[0].Session,
                matches.Length);
        }

        // Duplicate exact sources remain ambiguous unless playback identifies one candidate.
        return new SpotifySessionSelection(
            SpotifySessionSelectionStatus.Ambiguous,
            null,
            matches.Length);
    }
}
