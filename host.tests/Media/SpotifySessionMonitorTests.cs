using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media;

public sealed class SpotifySessionMonitorTests
{
    [Fact]
    public async Task InitializesAndSelectsOnlyVerifiedSpotifySession()
    {
        var chrome = new StubSession("Chrome", MediaSessionPlaybackStatus.Playing, Paused("Chrome"));
        var spotify = new StubSession("Spotify.exe", MediaSessionPlaybackStatus.Paused, Paused("Spotify.exe"));
        var manager = new StubManager([chrome, spotify]);
        await using var monitor = CreateMonitor(manager);

        var observation = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal("Spotify.exe", observation.SourceAppUserModelId);
        Assert.True(monitor.IsAvailable);
        Assert.Equal(SpotifySessionSelectionStatus.Selected, monitor.SelectionStatus);
        Assert.True(chrome.Disposed);
        Assert.False(spotify.Disposed);
    }

    [Fact]
    public async Task InitializesAndSelectsVerifiedStoreSpotifySession()
    {
        const string source = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
        var spotify = new StubSession(
            source,
            MediaSessionPlaybackStatus.Playing,
            Paused(source, "Store track"));
        var manager = new StubManager([spotify]);
        await using var monitor = CreateMonitor(manager);

        var observation = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal(source, observation.SourceAppUserModelId);
        Assert.Equal("Store track", observation.Track!.Title);
        Assert.Equal(SpotifySessionSelectionStatus.Selected, monitor.SelectionStatus);
    }

    [Fact]
    public async Task MissingAndAmbiguousSessionsReturnUnavailableWithoutGuessing()
    {
        var manager = new StubManager(
        [
            new StubSession("Chrome", MediaSessionPlaybackStatus.Playing, Paused("Chrome")),
        ]);
        await using var monitor = CreateMonitor(manager);

        var missing = await monitor.ReadAsync(CancellationToken.None);
        manager.SetSessions(
        [
            new StubSession("Spotify.exe", MediaSessionPlaybackStatus.Paused, Paused("Spotify.exe")),
            new StubSession("Spotify.exe", MediaSessionPlaybackStatus.Stopped, Paused("Spotify.exe")),
        ]);
        manager.RaiseChanged();
        var ambiguous = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, missing.Playback);
        Assert.Equal(PlaybackState.Unavailable, ambiguous.Playback);
        Assert.Equal(SpotifySessionSelectionStatus.Ambiguous, monitor.SelectionStatus);
        Assert.True(monitor.IsAvailable);
    }

    [Fact]
    public async Task SessionsChangedRebindsCurrentWrappersAndSignalsCoordinator()
    {
        var first = new StubSession("Spotify.exe", MediaSessionPlaybackStatus.Paused, Paused("Spotify.exe", "A"));
        var manager = new StubManager([first]);
        await using var monitor = CreateMonitor(manager);
        await monitor.ReadAsync(CancellationToken.None);
        var changed = 0;
        monitor.Changed += (_, _) => changed++;
        var replacement = new StubSession(
            "Spotify.exe",
            MediaSessionPlaybackStatus.Playing,
            Playing("B"));

        manager.SetSessions([replacement]);
        manager.RaiseChanged();
        var observation = await monitor.ReadAsync(CancellationToken.None);

        Assert.True(first.Disposed);
        Assert.False(replacement.Disposed);
        Assert.Equal("B", observation.Track!.Title);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task BindingGenerationRejectsLateReadFromReplacedSession()
    {
        var pending = new TaskCompletionSource<SessionObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new StubSession(
            "Spotify.exe",
            MediaSessionPlaybackStatus.Playing,
            _ => new ValueTask<SessionObservation>(pending.Task));
        var manager = new StubManager([first]);
        await using var monitor = CreateMonitor(manager);

        var read = monitor.ReadAsync(CancellationToken.None).AsTask();
        await first.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = new StubSession(
            "Spotify.exe",
            MediaSessionPlaybackStatus.Playing,
            Playing("New"));
        manager.SetSessions([replacement]);
        manager.RaiseChanged();
        pending.SetResult(Playing("Old"));

        var observation = await read.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("New", observation.Track!.Title);
        Assert.Equal(1, replacement.ReadCount);
    }

    [Fact]
    public async Task MediaEventsOnlySignalWithoutReadingOrCommitting()
    {
        var session = new StubSession(
            "Spotify.exe",
            MediaSessionPlaybackStatus.Paused,
            Paused("Spotify.exe"));
        var manager = new StubManager([session]);
        await using var monitor = CreateMonitor(manager);
        await monitor.ReadAsync(CancellationToken.None);
        var readsBeforeSignal = session.ReadCount;
        var changed = 0;
        monitor.Changed += (_, _) => changed++;

        session.RaiseChanged();

        Assert.Equal(1, changed);
        Assert.Equal(readsBeforeSignal, session.ReadCount);
    }

    [Fact]
    public async Task ManagerInitializationFailureIsReportedAsUnavailable()
    {
        var factory = new StubFactory(new InvalidOperationException("request failed"));
        await using var monitor = new SpotifySessionMonitor(
            factory,
            new SpotifySessionMatcher(),
            NullLogger<SpotifySessionMonitor>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await monitor.ReadAsync(CancellationToken.None));

        Assert.False(monitor.IsAvailable);
    }

    private static SpotifySessionMonitor CreateMonitor(StubManager manager)
    {
        return new SpotifySessionMonitor(
            new StubFactory(manager),
            new SpotifySessionMatcher(),
            NullLogger<SpotifySessionMonitor>.Instance);
    }

    private static SessionObservation Paused(string source, string? title = null)
    {
        return SessionObservation.Create(
            source,
            PlaybackState.Paused,
            title is null ? null : TrackMetadata.Create(title, "Artist", null));
    }

    private static SessionObservation Playing(string title)
    {
        return SessionObservation.Create(
            "Spotify.exe",
            PlaybackState.Playing,
            TrackMetadata.Create(title, "Artist", null));
    }

    private sealed class StubFactory : IMediaSessionManagerFactory
    {
        private readonly IMediaSessionManager? _manager;
        private readonly Exception? _error;

        public StubFactory(IMediaSessionManager manager)
        {
            _manager = manager;
        }

        public StubFactory(Exception error)
        {
            _error = error;
        }

        public ValueTask<IMediaSessionManager> CreateAsync(CancellationToken cancellationToken)
        {
            return _error is null
                ? ValueTask.FromResult(_manager!)
                : ValueTask.FromException<IMediaSessionManager>(_error);
        }
    }

    private sealed class StubManager(IReadOnlyList<IMediaSessionAdapter> sessions) : IMediaSessionManager
    {
        private IReadOnlyList<IMediaSessionAdapter> _sessions = sessions;

        public event EventHandler? SessionsChanged;

        public IReadOnlyList<IMediaSessionAdapter> GetSessions()
        {
            return _sessions;
        }

        public void SetSessions(IReadOnlyList<IMediaSessionAdapter> value)
        {
            _sessions = value;
        }

        public void RaiseChanged()
        {
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubSession : IMediaSessionAdapter
    {
        private readonly Func<CancellationToken, ValueTask<SessionObservation>> _read;

        public StubSession(
            string source,
            MediaSessionPlaybackStatus playbackStatus,
            SessionObservation observation)
            : this(source, playbackStatus, _ => ValueTask.FromResult(observation))
        {
        }

        public StubSession(
            string source,
            MediaSessionPlaybackStatus playbackStatus,
            Func<CancellationToken, ValueTask<SessionObservation>> read)
        {
            SourceAppUserModelId = source;
            PlaybackStatus = playbackStatus;
            _read = read;
        }

        public event EventHandler? Changed;

        public string SourceAppUserModelId { get; }

        public MediaSessionPlaybackStatus PlaybackStatus { get; }

        public bool Disposed { get; private set; }

        public int ReadCount { get; private set; }

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MediaSessionPlaybackStatus GetPlaybackStatus()
        {
            return PlaybackStatus;
        }

        public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            ReadStarted.TrySetResult();
            return _read(cancellationToken);
        }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
