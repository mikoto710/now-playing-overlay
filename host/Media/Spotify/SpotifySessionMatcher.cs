using NowPlayingOverlay.Host.Media.Windows;

namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed class SpotifySessionMatcher
{
    public const string VerifiedWin32SourceAppUserModelId = "Spotify.exe";
    public const string VerifiedStoreSourceAppUserModelId =
        "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";

    private static readonly HashSet<string> VerifiedSourceAppUserModelIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            VerifiedWin32SourceAppUserModelId,
            VerifiedStoreSourceAppUserModelId,
        };

    public SpotifySessionSelection Select(IEnumerable<SpotifySessionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var matches = candidates
            .Where(candidate => VerifiedSourceAppUserModelIds.Contains(
                candidate.SourceAppUserModelId))
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
