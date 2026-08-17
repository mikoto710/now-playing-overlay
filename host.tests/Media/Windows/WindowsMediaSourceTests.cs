using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.Windows;

public sealed class WindowsMediaSourceTests
{
    [Fact]
    public async Task InitializesAndSelectsOnlyTheExactConfiguredAumid()
    {
        var chrome = new StubSession("Chrome", MediaSessionPlaybackStatus.Playing, Paused("Chrome"));
        var selected = new StubSession("Player.App", MediaSessionPlaybackStatus.Paused, Paused("Player.App"));
        var manager = new StubManager([chrome, selected]);
        await using var monitor = CreateMonitor(manager);

        var observation = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal("Player.App", observation.Source!.Key.InstanceId);
        Assert.Equal(SourceStatus.Available, monitor.GetState().Status);
        Assert.True(chrome.Disposed);
        Assert.False(selected.Disposed);
    }

    [Fact]
    public async Task DiscoveryReturnsRawAumidsAsStableValues()
    {
        var manager = new StubManager([
            new StubSession("Z.Player", MediaSessionPlaybackStatus.Paused, Paused("Z.Player")),
            new StubSession("A.Player", MediaSessionPlaybackStatus.Playing, Playing("A.Player", "Track")),
        ]);
        await using var monitor = CreateMonitor(manager, selection: null);

        var discovery = await monitor.RefreshSourcesAsync();

        Assert.Equal(
            ["A.Player", "Z.Player"],
            discovery.Sources.Select(source => source.Key.InstanceId));
        Assert.Equal(SourceStatus.Unconfigured, discovery.State.Status);
    }

    [Fact]
    public async Task MissingAndAmbiguousSessionsReturnUnavailableWithoutGuessing()
    {
        var manager = new StubManager(
        [
            new StubSession("Other.Player", MediaSessionPlaybackStatus.Playing, Paused("Other.Player")),
        ]);
        await using var monitor = CreateMonitor(manager);

        var missing = await monitor.ReadAsync(CancellationToken.None);
        manager.SetSessions(
        [
            new StubSession("Player.App", MediaSessionPlaybackStatus.Paused, Paused("Player.App")),
            new StubSession("Player.App", MediaSessionPlaybackStatus.Stopped, Paused("Player.App")),
        ]);
        manager.RaiseChanged();
        var ambiguous = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, missing.Playback);
        Assert.Equal(PlaybackState.Unavailable, ambiguous.Playback);
        Assert.Equal("Player.App", missing.Source!.Key.InstanceId);
        Assert.Equal(SourceStatusReason.Ambiguous, monitor.GetState().Reason);
    }

    [Fact]
    public async Task SessionsChangedRebindsCurrentWrappersAndSignalsCoordinator()
    {
        var first = new StubSession("Player.App", MediaSessionPlaybackStatus.Paused, Paused("Player.App", "A"));
        var manager = new StubManager([first]);
        await using var monitor = CreateMonitor(manager);
        _ = await monitor.ReadAsync(CancellationToken.None);
        var changed = 0;
        monitor.Changed += (_, _) => changed++;
        var replacement = new StubSession(
            "Player.App",
            MediaSessionPlaybackStatus.Playing,
            Playing("Player.App", "B"));

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
            "Player.App",
            MediaSessionPlaybackStatus.Playing,
            _ => new ValueTask<SessionObservation>(pending.Task));
        var manager = new StubManager([first]);
        await using var monitor = CreateMonitor(manager);

        _ = await monitor.RefreshSourcesAsync();
        var read = monitor.ReadAsync(CancellationToken.None).AsTask();
        await first.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = new StubSession(
            "Player.App",
            MediaSessionPlaybackStatus.Playing,
            Playing("Player.App", "New"));
        manager.SetSessions([replacement]);
        manager.RaiseChanged();
        pending.SetResult(Playing("Player.App", "Old"));

        var observation = await read.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("New", observation.Track!.Title);
        Assert.Equal(1, replacement.ReadCount);
    }

    [Fact]
    public async Task MediaEventsOnlySignalWithoutReadingOrCommitting()
    {
        var session = new StubSession(
            "Player.App",
            MediaSessionPlaybackStatus.Paused,
            Paused("Player.App"));
        var manager = new StubManager([session]);
        await using var monitor = CreateMonitor(manager);
        _ = await monitor.ReadAsync(CancellationToken.None);
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
        await using var monitor = new WindowsMediaSource(
            factory,
            new WindowsMediaSessionMatcher(),
            NullLogger<WindowsMediaSource>.Instance);
        monitor.SetSelection(SourceDescriptor.WindowsMedia("Player.App"));

        var observation = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, observation.Playback);
        Assert.Equal(SourceStatusReason.PlatformUnavailable, monitor.GetState().Reason);
    }

    [Fact]
    public async Task RapidSelectionChangeRejectsLateObservationFromPreviousAumid()
    {
        var pending = new TaskCompletionSource<SessionObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new StubSession(
            "First.Player",
            MediaSessionPlaybackStatus.Playing,
            _ => new ValueTask<SessionObservation>(pending.Task));
        var second = new StubSession(
            "Second.Player",
            MediaSessionPlaybackStatus.Playing,
            Playing("Second.Player", "New"));
        var platform = new StubManager([first, second]);
        await using var monitor = CreateMonitor(platform, "First.Player");
        _ = await monitor.RefreshSourcesAsync();
        var oldRead = monitor.ReadAsync(CancellationToken.None).AsTask();
        await first.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        monitor.SetSelection(SourceDescriptor.WindowsMedia("Second.Player"));
        pending.SetResult(Playing("First.Player", "Old"));
        var resolved = await oldRead.WaitAsync(TimeSpan.FromSeconds(5));
        var current = resolved;
        for (var attempt = 0; attempt < 2 && current.Track is null; attempt++)
        {
            current = await monitor.ReadAsync(CancellationToken.None);
        }

        Assert.Equal("Second.Player", resolved.Source!.Key.InstanceId);
        Assert.Equal("Second.Player", current.Source!.Key.InstanceId);
        Assert.Equal("New", current.Track!.Title);
    }

    [Fact]
    public async Task ShutdownUnsubscribesAndDisposesManagerAndBoundSession()
    {
        var session = new StubSession(
            "Player.App",
            MediaSessionPlaybackStatus.Paused,
            Paused("Player.App", "Track"));
        var platform = new StubManager([session]);
        var monitor = CreateMonitor(platform);
        _ = await monitor.ReadAsync(CancellationToken.None);

        await monitor.DisposeAsync();

        Assert.True(platform.Disposed);
        Assert.True(session.Disposed);
        Assert.Equal(0, platform.SessionsChangedSubscribers);
        Assert.Equal(0, session.ChangedSubscribers);
    }

    private static WindowsMediaSource CreateMonitor(
        StubManager manager,
        string? selection = "Player.App")
    {
        var source = new WindowsMediaSource(
            new StubFactory(manager),
            new WindowsMediaSessionMatcher(),
            NullLogger<WindowsMediaSource>.Instance);
        source.SetSelection(
            selection is null ? null : SourceDescriptor.WindowsMedia(selection));
        return source;
    }

    private static SessionObservation Paused(string source, string? title = null)
    {
        return SessionObservation.Create(
            SourceDescriptor.WindowsMedia(source),
            PlaybackState.Paused,
            title is null ? null : TrackMetadata.Create(title, "Artist", null));
    }

    private static SessionObservation Playing(string source, string title)
    {
        return SessionObservation.Create(
            SourceDescriptor.WindowsMedia(source),
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
        private EventHandler? _sessionsChanged;
        private IReadOnlyList<IMediaSessionAdapter> _sessions = sessions;

        public event EventHandler? SessionsChanged
        {
            add
            {
                _sessionsChanged += value;
                SessionsChangedSubscribers++;
            }
            remove
            {
                _sessionsChanged -= value;
                SessionsChangedSubscribers--;
            }
        }

        public int SessionsChangedSubscribers { get; private set; }

        public bool Disposed { get; private set; }

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
            _sessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class StubSession : IMediaSessionAdapter
    {
        private readonly Func<CancellationToken, ValueTask<SessionObservation>> _read;
        private EventHandler? _changed;

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

        public event EventHandler? Changed
        {
            add
            {
                _changed += value;
                ChangedSubscribers++;
            }
            remove
            {
                _changed -= value;
                ChangedSubscribers--;
            }
        }

        public string SourceAppUserModelId { get; }

        public MediaSessionPlaybackStatus PlaybackStatus { get; }

        public bool Disposed { get; private set; }

        public int ChangedSubscribers { get; private set; }

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
            _changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
