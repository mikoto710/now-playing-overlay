using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify;

public sealed class SpotifySessionMatcherTests
{
    private readonly SpotifySessionMatcher _matcher = new();

    [Theory]
    [InlineData("spotify.EXE")]
    [InlineData("spotifyab.spotifymusic_ZPDNEKDRZREA0!spotify")]
    public void SelectsVerifiedExactSourcesCaseInsensitively(string source)
    {
        var chrome = Session("Chrome", MediaSessionPlaybackStatus.Playing);
        var prefix = Session("SpotifyAB.SpotifyMusic", MediaSessionPlaybackStatus.Playing);
        var spotify = Session(source, MediaSessionPlaybackStatus.Paused);

        var selection = _matcher.Select([chrome, prefix, spotify]);

        Assert.Equal(SpotifySessionSelectionStatus.Selected, selection.Status);
        Assert.Same(spotify.Session, selection.Session);
        Assert.Equal(1, selection.MatchCount);
    }

    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify-preview")]
    public void RejectsNearMatchesForVerifiedSources(string source)
    {
        var selection = _matcher.Select(
        [
            Session(source, MediaSessionPlaybackStatus.Playing),
        ]);

        Assert.Equal(SpotifySessionSelection.NotFound, selection);
    }

    [Fact]
    public void DoesNotFallBackToPlayingNonSpotifySession()
    {
        var selection = _matcher.Select(
        [
            Session("Chrome", MediaSessionPlaybackStatus.Playing),
            Session("OtherPlayer", MediaSessionPlaybackStatus.Playing),
        ]);

        Assert.Equal(SpotifySessionSelection.NotFound, selection);
    }

    [Fact]
    public void SelectsTheOnlyPlayingCandidateAmongDuplicateExactSources()
    {
        var paused = Session("Spotify.exe", MediaSessionPlaybackStatus.Paused);
        var playing = Session("SPOTIFY.EXE", MediaSessionPlaybackStatus.Playing);

        var selection = _matcher.Select([paused, playing]);

        Assert.Equal(SpotifySessionSelectionStatus.Selected, selection.Status);
        Assert.Same(playing.Session, selection.Session);
        Assert.Equal(2, selection.MatchCount);
    }

    [Fact]
    public void SelectsOnlyPlayingCandidateAcrossVerifiedInstallSources()
    {
        var win32 = Session("Spotify.exe", MediaSessionPlaybackStatus.Paused);
        var store = Session(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
            MediaSessionPlaybackStatus.Playing);

        var selection = _matcher.Select([win32, store]);

        Assert.Equal(SpotifySessionSelectionStatus.Selected, selection.Status);
        Assert.Same(store.Session, selection.Session);
        Assert.Equal(2, selection.MatchCount);
    }

    [Theory]
    [InlineData((int)MediaSessionPlaybackStatus.Paused, (int)MediaSessionPlaybackStatus.Paused)]
    [InlineData((int)MediaSessionPlaybackStatus.Playing, (int)MediaSessionPlaybackStatus.Playing)]
    public void KeepsDuplicateSourcesAmbiguousWithoutUniquePlayingCandidate(
        int firstValue,
        int secondValue)
    {
        var first = (MediaSessionPlaybackStatus)firstValue;
        var second = (MediaSessionPlaybackStatus)secondValue;
        var selection = _matcher.Select(
        [
            Session("Spotify.exe", first),
            Session("Spotify.exe", second),
        ]);

        Assert.Equal(SpotifySessionSelectionStatus.Ambiguous, selection.Status);
        Assert.Null(selection.Session);
        Assert.Equal(2, selection.MatchCount);
    }

    [Fact]
    public void SelectsSingleExactCandidateWhenPlaybackStatusCannotBeRead()
    {
        var spotify = Session("Spotify.exe", null);

        var selection = _matcher.Select([spotify]);

        Assert.Same(spotify.Session, selection.Session);
    }

    private static SpotifySessionCandidate Session(
        string source,
        MediaSessionPlaybackStatus? playbackStatus)
    {
        return new SpotifySessionCandidate(
            new StubSession(source),
            source,
            playbackStatus);
    }

    private sealed class StubSession(string source) : IMediaSessionAdapter
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public string SourceAppUserModelId => source;

        public MediaSessionPlaybackStatus GetPlaybackStatus()
        {
            return MediaSessionPlaybackStatus.Paused;
        }

        public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                SessionObservation.Create(source, PlaybackState.Paused));
        }

        public void Dispose()
        {
        }
    }
}
