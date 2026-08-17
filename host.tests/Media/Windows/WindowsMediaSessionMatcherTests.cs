using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.Windows;

public sealed class WindowsMediaSessionMatcherTests
{
    private readonly WindowsMediaSessionMatcher _matcher = new();

    [Fact]
    public void SelectsOnlyTheExactConfiguredAumid()
    {
        var selected = Session("Player.App!Main", MediaSessionPlaybackStatus.Paused);

        var selection = _matcher.Select(
            "Player.App!Main",
            [
                Session("Other.Player", MediaSessionPlaybackStatus.Playing),
                Session("player.app!main", MediaSessionPlaybackStatus.Playing),
                selected,
            ]);

        Assert.Equal(WindowsMediaSessionSelectionStatus.Selected, selection.Status);
        Assert.Same(selected.Session, selection.Session);
        Assert.Equal(1, selection.MatchCount);
    }

    [Fact]
    public void MissingSelectionDoesNotFallBackToAnotherPlayingSession()
    {
        var selection = _matcher.Select(
            "Missing.Player",
            [Session("Other.Player", MediaSessionPlaybackStatus.Playing)]);

        Assert.Equal(WindowsMediaSessionSelection.Missing, selection);
    }

    [Fact]
    public void SelectsTheOnlyPlayingSessionAmongDuplicateExactAumids()
    {
        var paused = Session("Player.App", MediaSessionPlaybackStatus.Paused);
        var playing = Session("Player.App", MediaSessionPlaybackStatus.Playing);

        var selection = _matcher.Select("Player.App", [paused, playing]);

        Assert.Equal(WindowsMediaSessionSelectionStatus.Selected, selection.Status);
        Assert.Same(playing.Session, selection.Session);
        Assert.Equal(2, selection.MatchCount);
    }

    [Theory]
    [InlineData((int)MediaSessionPlaybackStatus.Paused, (int)MediaSessionPlaybackStatus.Stopped)]
    [InlineData((int)MediaSessionPlaybackStatus.Playing, (int)MediaSessionPlaybackStatus.Playing)]
    public void DuplicateSessionsRemainAmbiguousWithoutOnePlayingCandidate(
        int firstValue,
        int secondValue)
    {
        var selection = _matcher.Select(
            "Player.App",
            [
                Session("Player.App", (MediaSessionPlaybackStatus)firstValue),
                Session("Player.App", (MediaSessionPlaybackStatus)secondValue),
            ]);

        Assert.Equal(WindowsMediaSessionSelectionStatus.Ambiguous, selection.Status);
        Assert.Null(selection.Session);
        Assert.Equal(2, selection.MatchCount);
    }

    [Fact]
    public void SingleExactSessionDoesNotRequireReadablePlaybackStatus()
    {
        var session = Session("Player.App", playbackStatus: null);

        var selection = _matcher.Select("Player.App", [session]);

        Assert.Same(session.Session, selection.Session);
    }

    private static WindowsMediaSessionCandidate Session(
        string source,
        MediaSessionPlaybackStatus? playbackStatus)
    {
        return new WindowsMediaSessionCandidate(new StubSession(source), source, playbackStatus);
    }

    private sealed class StubSession(string source) : IMediaSessionAdapter
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public string SourceAppUserModelId => source;

        public MediaSessionPlaybackStatus GetPlaybackStatus() => MediaSessionPlaybackStatus.Paused;

        public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                SessionObservation.Create(
                    SourceDescriptor.WindowsMedia(source),
                    PlaybackState.Paused));
        }

        public void Dispose()
        {
        }
    }
}
