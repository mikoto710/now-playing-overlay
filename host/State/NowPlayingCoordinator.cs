using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.State;

internal sealed class NowPlayingCoordinator : IAsyncDisposable
{
    private readonly object _lifecycleGate = new();
    private readonly ISessionSource _source;
    private readonly NowPlayingStore _store;
    private readonly ArtworkCache _artworkCache;
    private readonly NowPlayingCoordinatorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delay;
    private readonly ILogger<NowPlayingCoordinator> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<bool> _refreshSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Channel<CoordinatorMessage> _messages = Channel.CreateUnbounded<CoordinatorMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly HashSet<Task> _artworkTasks = [];
    private CancellationTokenSource? _activeReadCancellation;
    private Task? _signalPump;
    private Task? _worker;
    private Exception? _lastError;
    private long _generation;
    private long _pendingReadGeneration;
    private long _artworkRevision;
    private string? _previousArtworkId;
    private bool _readMessageQueued;
    private bool _started;
    private bool _disposed;

    public NowPlayingCoordinator(
        ISessionSource source,
        NowPlayingStore store,
        ArtworkCache artworkCache,
        NowPlayingCoordinatorOptions? options = null,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, ValueTask>? delay = null,
        ILogger<NowPlayingCoordinator>? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _artworkCache = artworkCache ?? throw new ArgumentNullException(nameof(artworkCache));
        _options = options ?? new NowPlayingCoordinatorOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delay = delay ?? DefaultDelayAsync;
        _logger = logger ?? NullLogger<NowPlayingCoordinator>.Instance;
        _artworkRevision = store.Current.Artwork?.ArtworkRevision ?? 0;
    }

    public Exception? LastError
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _lastError;
            }
        }
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("Coordinator has already started.");
            }

            _started = true;
            _source.Changed += OnSourceChanged;
            _signalPump = PumpRefreshSignalsAsync(_shutdown.Token);
            _worker = ProcessMessagesAsync(_shutdown.Token);
        }

        RequestRefresh();
    }

    public void RequestRefresh()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                throw new InvalidOperationException("Coordinator must be started before requesting refresh.");
            }

            checked
            {
                _generation++;
            }

            // Cancellation is an optimization; generation rejects sources that ignore it.
            _activeReadCancellation?.Cancel();
            _refreshSignals.Writer.TryWrite(true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? signalPump;
        Task? worker;
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_started)
            {
                _source.Changed -= OnSourceChanged;
            }

            _shutdown.Cancel();
            _activeReadCancellation?.Cancel();
            _refreshSignals.Writer.TryComplete();
            _messages.Writer.TryComplete();
            signalPump = _signalPump;
            worker = _worker;
        }

        await IgnoreCancellationAsync(signalPump);
        await IgnoreCancellationAsync(worker);
        Task[] artworkTasks;
        lock (_lifecycleGate)
        {
            artworkTasks = _artworkTasks.ToArray();
        }

        await Task.WhenAll(artworkTasks.Select(IgnoreCancellationAsync));
        await _source.DisposeAsync();
        _shutdown.Dispose();
    }

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        try
        {
            RequestRefresh();
        }
        catch (ObjectDisposedException)
        {
            // A final platform event may race with unsubscription during shutdown.
        }
    }

    private async Task PumpRefreshSignalsAsync(CancellationToken cancellationToken)
    {
        while (await _refreshSignals.Reader.WaitToReadAsync(cancellationToken))
        {
            DrainRefreshSignals();
            // Trailing debounce restarts until the latest burst becomes quiet.
            while (true)
            {
                await _delay(_options.DebounceDelay, cancellationToken);
                if (!DrainRefreshSignals())
                {
                    break;
                }
            }

            QueueLatestRead(GetGeneration());
        }
    }

    private bool DrainRefreshSignals()
    {
        var found = false;
        while (_refreshSignals.Reader.TryRead(out _))
        {
            found = true;
        }

        return found;
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            switch (message)
            {
                case ReadRequested:
                    await ProcessReadAsync(TakePendingReadGeneration(), cancellationToken);
                    break;
                case ArtworkCompleted artworkCompleted:
                    ProcessArtworkCompletion(artworkCompleted);
                    break;
            }
        }
    }

    private async Task ProcessReadAsync(long generation, CancellationToken cancellationToken)
    {
        if (generation != GetGeneration())
        {
            return;
        }

        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_lifecycleGate)
        {
            _activeReadCancellation = readCancellation;
        }

        SessionObservation observation;
        try
        {
            observation = await _source.ReadAsync(readCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (readCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException error)
        {
            SetLastError(error);
            return;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            SetLastError(error);
            return;
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(_activeReadCancellation, readCancellation))
                {
                    _activeReadCancellation = null;
                }
            }
        }

        if (generation != GetGeneration())
        {
            return;
        }

        SetLastError(null);
        var before = _store.Current;
        // A new identity clears stale artwork before its replacement arrives.
        var preserveArtwork = observation.ArtworkReader is not null
            && Equals(before.Identity, observation.Identity);
        var artwork = preserveArtwork ? before.Artwork : null;
        _store.TryCommit(
            observation.SourceAppUserModelId,
            observation.Playback,
            observation.Track,
            artwork,
            _timeProvider.GetUtcNow(),
            out var committed);
        UpdateProtectedArtwork(before, committed);

        if (observation.ArtworkReader is not null && observation.Identity is not null)
        {
            StartArtworkRead(observation.ArtworkReader, generation, observation.Identity, cancellationToken);
        }
    }

    private void StartArtworkRead(
        IArtworkReader reader,
        long generation,
        TrackIdentity identity,
        CancellationToken cancellationToken)
    {
        var task = ReadArtworkAsync(reader, generation, identity, cancellationToken);
        lock (_lifecycleGate)
        {
            _artworkTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_lifecycleGate)
                {
                    _artworkTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReadArtworkAsync(
        IArtworkReader reader,
        long generation,
        TrackIdentity identity,
        CancellationToken cancellationToken)
    {
        ArtworkPayload? payload;
        try
        {
            payload = await reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            payload = null;
        }

        try
        {
            await _messages.Writer.WriteAsync(
                new ArtworkCompleted(generation, identity, payload),
                cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // Shutdown completed before the asynchronous artwork read.
        }
    }

    private void ProcessArtworkCompletion(ArtworkCompleted completion)
    {
        if (completion.Generation != GetGeneration())
        {
            return;
        }

        var current = _store.Current;
        if (!Equals(current.Identity, completion.Identity))
        {
            return;
        }

        // Cache insertion precedes publication, so snapshots never reference missing bytes.
        if (completion.Payload is null
            || !_artworkCache.TryAdd(completion.Payload, out var cacheEntry))
        {
            _store.TryCommit(
                current.SourceAppUserModelId,
                current.Playback,
                current.Track,
                artwork: null,
                _timeProvider.GetUtcNow(),
                out var withoutArtwork);
            UpdateProtectedArtwork(current, withoutArtwork);
            return;
        }

        if (completion.Generation != GetGeneration()
            || !Equals(_store.Current.Identity, completion.Identity))
        {
            return;
        }

        current = _store.Current;
        if (string.Equals(current.Artwork?.ArtworkId, cacheEntry!.ArtworkId, StringComparison.Ordinal))
        {
            return;
        }

        var descriptor = new ArtworkDescriptor(
            checked(++_artworkRevision),
            cacheEntry.ArtworkId,
            cacheEntry.ContentType,
            cacheEntry.ByteLength);
        _store.TryCommit(
            current.SourceAppUserModelId,
            current.Playback,
            current.Track,
            descriptor,
            _timeProvider.GetUtcNow(),
            out var withArtwork);
        UpdateProtectedArtwork(current, withArtwork);
    }

    private void UpdateProtectedArtwork(NowPlayingSnapshot before, NowPlayingSnapshot after)
    {
        // Keep current and previous bytes available during frontend transitions.
        var beforeId = before.Artwork?.ArtworkId;
        var afterId = after.Artwork?.ArtworkId;
        if (!string.Equals(beforeId, afterId, StringComparison.Ordinal) && beforeId is not null)
        {
            _previousArtworkId = beforeId;
        }

        _artworkCache.SetProtectedIds(afterId, _previousArtworkId);
    }

    private long GetGeneration()
    {
        lock (_lifecycleGate)
        {
            return _generation;
        }
    }

    private void QueueLatestRead(long generation)
    {
        lock (_lifecycleGate)
        {
            _pendingReadGeneration = generation;
            if (_readMessageQueued)
            {
                return;
            }

            // One marker represents the latest pending read, bounding slow-source backlog.
            _readMessageQueued = true;
            _messages.Writer.TryWrite(new ReadRequested());
        }
    }

    private long TakePendingReadGeneration()
    {
        lock (_lifecycleGate)
        {
            var generation = _pendingReadGeneration;
            _readMessageQueued = false;
            return generation;
        }
    }

    private void SetLastError(Exception? error)
    {
        Exception? previous;
        lock (_lifecycleGate)
        {
            previous = _lastError;
            _lastError = error;
        }

        if (error is not null
            && (previous is null
                || previous.GetType() != error.GetType()
                || !string.Equals(previous.Message, error.Message, StringComparison.Ordinal)))
        {
            _logger.LogError(error, "The now-playing coordinator could not read the media session.");
        }
        else if (error is null && previous is not null)
        {
            _logger.LogInformation("The now-playing coordinator recovered after a media session read failure.");
        }
    }

    private static ValueTask DefaultDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return new ValueTask(Task.Delay(delay, cancellationToken));
    }

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private abstract record CoordinatorMessage;

    private sealed record ReadRequested : CoordinatorMessage;

    private sealed record ArtworkCompleted(
        long Generation,
        TrackIdentity Identity,
        ArtworkPayload? Payload) : CoordinatorMessage;
}
