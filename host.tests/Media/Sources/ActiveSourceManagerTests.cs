using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.Sources;

public sealed class ActiveSourceManagerTests
{
    [Fact]
    public async Task SelectionCommitsUnavailableBeforeReadingTheActiveProvider()
    {
        var provider = new StubProvider();
        await using var manager = new ActiveSourceManager(
            [provider],
            SourceDescriptor.WindowsMedia("Player.App"));
        provider.SetObservation(Playing("Player.App", "Track"));

        var transition = await manager.ReadAsync(CancellationToken.None);
        var current = await manager.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, transition.Playback);
        Assert.Equal("Player.App", transition.Source!.Key.InstanceId);
        Assert.Equal("Track", current.Track!.Title);
        Assert.Equal(SourceStatus.Available, manager.GetState().Status);
        Assert.Equal(1, provider.ReadCount);
    }

    [Fact]
    public async Task SelectionGenerationRejectsALateResultFromThePreviousSelection()
    {
        var pending = new TaskCompletionSource<SessionObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new StubProvider
        {
            ReadHandler = _ => new ValueTask<SessionObservation>(pending.Task),
        };
        await using var manager = new ActiveSourceManager(
            [provider],
            SourceDescriptor.WindowsMedia("First.Player"));
        _ = await manager.ReadAsync(CancellationToken.None);
        var oldRead = manager.ReadAsync(CancellationToken.None).AsTask();
        await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        manager.Select(SourceDescriptor.WindowsMedia("Second.Player"));
        provider.SetObservation(Playing("Second.Player", "New"));
        pending.SetResult(Playing("First.Player", "Old"));
        var rejected = await oldRead.WaitAsync(TimeSpan.FromSeconds(5));
        _ = await manager.ReadAsync(CancellationToken.None);
        var current = await manager.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, rejected.Playback);
        Assert.Equal("Second.Player", rejected.Source!.Key.InstanceId);
        Assert.Equal("New", current.Track!.Title);
    }

    [Fact]
    public async Task DeactivationStopsForwardingProviderSignalsAndDisposalOwnsProviderLifetime()
    {
        var provider = new StubProvider();
        var manager = new ActiveSourceManager(
            [provider],
            SourceDescriptor.WindowsMedia("Player.App"));
        var changes = 0;
        manager.Changed += (_, _) => changes++;

        provider.RaiseChanged();
        manager.Select(null);
        provider.RaiseChanged();

        Assert.Equal(2, changes);
        Assert.Null(provider.Selection);
        Assert.Equal(SourceStatus.Unconfigured, manager.GetState().Status);

        await manager.DisposeAsync();

        Assert.True(provider.Disposed);
    }

    private static SessionObservation Playing(string source, string title)
    {
        return SessionObservation.Create(
            SourceDescriptor.WindowsMedia(source),
            PlaybackState.Playing,
            TrackMetadata.Create(title, "Artist", null));
    }

    private sealed class StubProvider : IMediaSourceProvider
    {
        private SessionObservation _observation =
            SessionObservation.Create(null, PlaybackState.Unavailable);

        public event EventHandler? Changed;

        public SourceProvider Provider => SourceProvider.WindowsMedia;

        public SourceDescriptor? Selection { get; private set; }

        public bool Disposed { get; private set; }

        public int ReadCount { get; private set; }

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<CancellationToken, ValueTask<SessionObservation>>? ReadHandler { get; set; }

        public SourceManagerState GetState()
        {
            return Selection is null
                ? SourceManagerState.Unconfigured
                : new SourceManagerState(
                    Selection,
                    _observation.Playback == PlaybackState.Unavailable
                        ? SourceStatus.Starting
                        : SourceStatus.Available,
                    _observation.Playback == PlaybackState.Unavailable
                        ? SourceStatusReason.Starting
                        : SourceStatusReason.None);
        }

        public void SetSelection(SourceDescriptor? selection)
        {
            Selection = selection;
            _observation = SessionObservation.Create(selection, PlaybackState.Unavailable);
        }

        public void SetObservation(SessionObservation observation)
        {
            _observation = observation;
            ReadHandler = _ => ValueTask.FromResult(_observation);
        }

        public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            ReadStarted.TrySetResult();
            return ReadHandler?.Invoke(cancellationToken)
                ?? ValueTask.FromResult(_observation);
        }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
