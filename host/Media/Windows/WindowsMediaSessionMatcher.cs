namespace NowPlayingOverlay.Host.Media.Windows;

internal sealed class WindowsMediaSessionMatcher
{
    public WindowsMediaSessionSelection Select(
        string sourceAppUserModelId,
        IEnumerable<WindowsMediaSessionCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAppUserModelId);
        ArgumentNullException.ThrowIfNull(candidates);
        var matches = candidates
            .Where(candidate => string.Equals(
                candidate.SourceAppUserModelId,
                sourceAppUserModelId,
                StringComparison.Ordinal))
            .ToArray();

        if (matches.Length == 0)
        {
            return WindowsMediaSessionSelection.Missing;
        }

        if (matches.Length == 1)
        {
            return new WindowsMediaSessionSelection(
                WindowsMediaSessionSelectionStatus.Selected,
                matches[0].Session,
                matches.Length);
        }

        var playing = matches
            .Where(candidate => candidate.PlaybackStatus == MediaSessionPlaybackStatus.Playing)
            .ToArray();
        return playing.Length == 1
            ? new WindowsMediaSessionSelection(
                WindowsMediaSessionSelectionStatus.Selected,
                playing[0].Session,
                matches.Length)
            : new WindowsMediaSessionSelection(
                WindowsMediaSessionSelectionStatus.Ambiguous,
                Session: null,
                matches.Length);
    }
}

internal sealed record WindowsMediaSessionCandidate(
    IMediaSessionAdapter Session,
    string SourceAppUserModelId,
    MediaSessionPlaybackStatus? PlaybackStatus);

internal sealed record WindowsMediaSessionSelection(
    WindowsMediaSessionSelectionStatus Status,
    IMediaSessionAdapter? Session,
    int MatchCount)
{
    public static WindowsMediaSessionSelection Missing { get; } =
        new(WindowsMediaSessionSelectionStatus.Missing, null, 0);
}

internal enum WindowsMediaSessionSelectionStatus
{
    Missing,
    Selected,
    Ambiguous,
}
