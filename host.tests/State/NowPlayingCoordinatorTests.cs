using System.Threading.Channels;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Tests.State;

public sealed class NowPlayingCoordinatorTests
{
    private static readonly Guid ServerInstanceId = Guid.Parse("f2fd945a-9beb-4572-a0df-d84ffc24b0c4");
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task DebounceCoalescesRepeatedSignalsIntoOneReadAndCommit()
    {
        var source = new ControlledSessionSource();
        source.Enqueue(Playing("A"));
        var delay = new ControlledDelay();
        var store = CreateStore();
        await using var coordinator = CreateCoordinator(source, store, delay.DelayAsync);
        using var subscription = store.Subscribe();

        coordinator.Start();
        await delay.WaitForRequestAsync();
        source.RaiseChanged();
        source.RaiseChanged();
        delay.Release();
        await delay.WaitForRequestAsync();
        delay.Release();

        var snapshot = await WaitForSnapshotAsync(
            subscription,
            value => value.Track?.Title == "A");

        Assert.Equal(1, snapshot.SnapshotRevision);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(ObservedAt, snapshot.ObservedAt);
    }

    [Fact]
    public async Task LatestGenerationDiscardsSlowAAndCommitsOnlyC()
    {
        var source = new ControlledSessionSource();
        var slowA = source.EnqueuePending();
        source.Enqueue(Playing("C"));
        var store = CreateStore();
        await using var coordinator = CreateCoordinator(source, store);
        using var subscription = store.Subscribe();

        coordinator.Start();
        await source.WaitForReadAsync(1);
        source.RaiseChanged();
        source.RaiseChanged();
        slowA.SetResult(Playing("A"));

        var snapshot = await WaitForSnapshotAsync(
            subscription,
            value => value.Track?.Title == "C");

        Assert.Equal(1, snapshot.SnapshotRevision);
        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public async Task OldArtworkCompletionCannotAttachToNewTrack()
    {
        var source = new ControlledSessionSource();
        var oldArtwork = new ControlledArtworkReader();
        source.Enqueue(Playing("A", oldArtwork));
        source.Enqueue(Playing("B"));
        source.Enqueue(SessionObservation.Create(null, PlaybackState.Unavailable));
        var store = CreateStore();
        await using var coordinator = CreateCoordinator(source, store);
        using var subscription = store.Subscribe();

        coordinator.Start();
        await WaitForSnapshotAsync(subscription, value => value.Track?.Title == "A");
        await oldArtwork.Started.Task.WaitAsync(TestTimeout);
        source.RaiseChanged();
        await WaitForSnapshotAsync(subscription, value => value.Track?.Title == "B");
        oldArtwork.Complete(ArtworkPayload.Create(OnePixelPng));
        await oldArtwork.Finished.Task.WaitAsync(TestTimeout);
        source.RaiseChanged();

        var unavailable = await WaitForSnapshotAsync(
            subscription,
            value => value.Playback == PlaybackState.Unavailable);

        Assert.Equal(3, unavailable.SnapshotRevision);
        Assert.Null(unavailable.Artwork);
    }

    [Fact]
    public async Task IdenticalArtworkHashDoesNotIncrementArtworkOrSnapshotRevision()
    {
        var source = new ControlledSessionSource();
        source.Enqueue(Playing("A", new ImmediateArtworkReader(OnePixelPng)));
        source.Enqueue(Playing("A", new ImmediateArtworkReader(OnePixelPng)));
        source.Enqueue(SessionObservation.Create(null, PlaybackState.Unavailable));
        var store = CreateStore();
        await using var coordinator = CreateCoordinator(source, store);
        using var subscription = store.Subscribe();

        coordinator.Start();
        var withArtwork = await WaitForSnapshotAsync(subscription, value => value.Artwork is not null);
        source.RaiseChanged();
        await source.WaitForReadAsync(2);
        source.RaiseChanged();

        var unavailable = await WaitForSnapshotAsync(
            subscription,
            value => value.Playback == PlaybackState.Unavailable);

        Assert.Equal(1, withArtwork.Artwork!.ArtworkRevision);
        Assert.Equal(2, withArtwork.SnapshotRevision);
        Assert.Equal(3, unavailable.SnapshotRevision);
    }

    [Fact]
    public async Task ArtworkBytesExistBeforeSnapshotPublishesTheirDescriptor()
    {
        var source = new ControlledSessionSource();
        source.Enqueue(Playing("A", new ImmediateArtworkReader(OnePixelPng)));
        var store = CreateStore();
        var cache = new ArtworkCache();
        await using var coordinator = new NowPlayingCoordinator(
            source,
            store,
            cache,
            new NowPlayingCoordinatorOptions { DebounceDelay = TimeSpan.Zero },
            new FixedTimeProvider(),
            delay: NoDelayAsync);
        using var subscription = store.Subscribe();

        coordinator.Start();
        var snapshot = await WaitForSnapshotAsync(subscription, value => value.Artwork is not null);

        Assert.True(cache.TryGet(snapshot.Artwork!.ArtworkId, out var entry));
        Assert.Equal(snapshot.Artwork.ByteLength, entry!.ByteLength);
    }

    [Fact]
    public async Task PlaybackTransitionsPreserveIdentityUntilIdleOrUnavailable()
    {
        var source = new ControlledSessionSource();
        var track = TrackMetadata.Create("A", "Artist", null);
        source.Enqueue(SessionObservation.Create("Spotify.exe", PlaybackState.Playing, track));
        source.Enqueue(SessionObservation.Create("Spotify.exe", PlaybackState.Paused, track));
        source.Enqueue(SessionObservation.Create("Spotify.exe", PlaybackState.Stopped, track));
        source.Enqueue(SessionObservation.Create("Spotify.exe", PlaybackState.Idle));
        source.Enqueue(SessionObservation.Create(null, PlaybackState.Unavailable));
        var store = CreateStore();
        await using var coordinator = CreateCoordinator(source, store);
        using var subscription = store.Subscribe();

        coordinator.Start();
        var playing = await WaitForSnapshotAsync(subscription, value => value.Playback == PlaybackState.Playing);
        source.RaiseChanged();
        var paused = await WaitForSnapshotAsync(subscription, value => value.Playback == PlaybackState.Paused);
        source.RaiseChanged();
        var stopped = await WaitForSnapshotAsync(subscription, value => value.Playback == PlaybackState.Stopped);
        source.RaiseChanged();
        var idle = await WaitForSnapshotAsync(subscription, value => value.Playback == PlaybackState.Idle);
        source.RaiseChanged();
        var unavailable = await WaitForSnapshotAsync(
            subscription,
            value => value.Playback == PlaybackState.Unavailable);

        Assert.Equal(playing.Identity, paused.Identity);
        Assert.Equal(playing.Identity, stopped.Identity);
        Assert.Null(idle.Identity);
        Assert.Null(unavailable.Identity);
        Assert.Equal(5, unavailable.SnapshotRevision);
    }

    [Fact]
    public async Task SourceFailureIsReportedWithoutPretendingSpotifyIsUnavailable()
    {
        var source = new ControlledSessionSource();
        source.EnqueueException(new InvalidOperationException("read failed"));
        var store = CreateStore();
        await using var coordinator = CreateCoordinator(source, store);

        coordinator.Start();
        await source.WaitForReadAsync(1);
        await source.WaitForCompletionAsync(1);

        Assert.IsType<InvalidOperationException>(coordinator.LastError);
        Assert.Equal(0, store.Current.SnapshotRevision);
    }

    private static TimeSpan TestTimeout => TimeSpan.FromSeconds(5);

    private static NowPlayingCoordinator CreateCoordinator(
        ControlledSessionSource source,
        NowPlayingStore store,
        Func<TimeSpan, CancellationToken, ValueTask>? delay = null)
    {
        return new NowPlayingCoordinator(
            source,
            store,
            new ArtworkCache(),
            new NowPlayingCoordinatorOptions { DebounceDelay = TimeSpan.Zero },
            new FixedTimeProvider(),
            delay: delay ?? NoDelayAsync);
    }

    private static NowPlayingStore CreateStore()
    {
        return new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(ServerInstanceId, DateTimeOffset.UtcNow));
    }

    private static SessionObservation Playing(string title, IArtworkReader? artworkReader = null)
    {
        return SessionObservation.Create(
            "Spotify.exe",
            PlaybackState.Playing,
            TrackMetadata.Create(title, "Artist", null),
            artworkReader);
    }

    private static async Task<NowPlayingSnapshot> WaitForSnapshotAsync(
        NowPlayingSubscription subscription,
        Func<NowPlayingSnapshot, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await foreach (var snapshot in subscription.Reader.ReadAllAsync(cancellation.Token))
        {
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException("Subscription completed before the expected snapshot.");
    }

    private static ValueTask NoDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    private sealed class ControlledDelay
    {
        private readonly Channel<TaskCompletionSource> _requests = Channel.CreateUnbounded<TaskCompletionSource>();
        private TaskCompletionSource? _current;

        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _requests.Writer.WriteAsync(completion, cancellationToken);
            await completion.Task.WaitAsync(cancellationToken);
        }

        public async Task WaitForRequestAsync()
        {
            _current = await _requests.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
        }

        public void Release()
        {
            Assert.NotNull(_current);
            _current.SetResult();
            _current = null;
        }
    }

    private sealed class ControlledSessionSource : ISessionSource
    {
        private readonly object _gate = new();
        private readonly Queue<Func<CancellationToken, ValueTask<SessionObservation>>> _reads = [];
        private readonly Channel<int> _readStarted = Channel.CreateUnbounded<int>();
        private readonly Channel<int> _readCompleted = Channel.CreateUnbounded<int>();

        public event EventHandler? Changed;

        public int ReadCount { get; private set; }

        public void Enqueue(SessionObservation observation)
        {
            lock (_gate)
            {
                _reads.Enqueue(_ => ValueTask.FromResult(observation));
            }
        }

        public TaskCompletionSource<SessionObservation> EnqueuePending()
        {
            var completion = new TaskCompletionSource<SessionObservation>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _reads.Enqueue(_ => new ValueTask<SessionObservation>(completion.Task));
            }

            return completion;
        }

        public void EnqueueException(Exception error)
        {
            lock (_gate)
            {
                _reads.Enqueue(_ => ValueTask.FromException<SessionObservation>(error));
            }
        }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public async ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
        {
            Func<CancellationToken, ValueTask<SessionObservation>> read;
            int readNumber;
            lock (_gate)
            {
                read = _reads.Dequeue();
                readNumber = ++ReadCount;
            }

            _readStarted.Writer.TryWrite(readNumber);
            try
            {
                return await read(cancellationToken);
            }
            finally
            {
                _readCompleted.Writer.TryWrite(readNumber);
            }
        }

        public async Task WaitForReadAsync(int expectedRead)
        {
            while (await _readStarted.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout) < expectedRead)
            {
            }
        }

        public async Task WaitForCompletionAsync(int expectedRead)
        {
            while (await _readCompleted.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout) < expectedRead)
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledArtworkReader : IArtworkReader
    {
        private readonly TaskCompletionSource<ArtworkPayload?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(ArtworkPayload? payload)
        {
            _completion.SetResult(payload);
        }

        public async ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                return await _completion.Task;
            }
            finally
            {
                Finished.SetResult();
            }
        }
    }

    private sealed class ImmediateArtworkReader(byte[] bytes) : IArtworkReader
    {
        public ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<ArtworkPayload?>(ArtworkPayload.Create(bytes));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return ObservedAt;
        }
    }
}
