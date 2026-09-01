using System.Runtime.ExceptionServices;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Sources;

/// <summary>
/// Owns all providers and exposes exactly one selected provider as an <see cref="ISessionSource"/>.
/// </summary>
/// <remarks>
/// No selection -> Unconfigured. A new selection first returns Unavailable to clear old media,
/// then schedules a follow-up read from that provider. Selection generation rejects reads and
/// follow-ups from older selections; cancellation only shortens their work. Provider Changed
/// events are invalidation signals, not data, and are forwarded outside the gate. Disposal owns
/// every registered provider and is terminal.
/// </remarks>
internal sealed class ActiveSourceManager : ISessionSource, ISessionSourceStatus
{
    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<SourceProvider, IMediaSourceProvider> _providers;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task> _followUpRefreshes = [];
    private IMediaSourceProvider? _activeProvider;
    private SourceDescriptor? _selection;
    private CancellationTokenSource? _activeReadCancellation;
    // Rejects reads and follow-ups created for an earlier selection.
    private long _selectionGeneration;
    private bool _transitionPending;
    private bool _disposeStarted;
    private bool _disposed;

    public ActiveSourceManager(
        IEnumerable<IMediaSourceProvider> providers,
        SourceDescriptor? initialSelection)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var providerMap = new Dictionary<SourceProvider, IMediaSourceProvider>();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (!providerMap.TryAdd(provider.Provider, provider))
            {
                throw new ArgumentException(
                    $"Only one media source provider may be registered for {provider.Provider}.",
                    nameof(providers));
            }
        }

        _providers = providerMap;
        if (initialSelection is not null)
        {
            Select(initialSelection, notify: false);
        }
    }

    public event EventHandler? Changed;

    public SourceManagerState GetState()
    {
        while (true)
        {
            IMediaSourceProvider? provider;
            long generation;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                provider = _activeProvider;
                generation = _selectionGeneration;
            }

            if (provider is null)
            {
                return SourceManagerState.Unconfigured;
            }

            var state = provider.GetState();
            lock (_gate)
            {
                if (_selectionGeneration == generation
                    && ReferenceEquals(_activeProvider, provider))
                {
                    return state;
                }
            }
        }
    }

    public void Select(SourceDescriptor? selection)
    {
        Select(selection, notify: true);
    }

    public async ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        IMediaSourceProvider? provider;
        SourceDescriptor? transitionSource;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            provider = _activeProvider;
            generation = _selectionGeneration;
            transitionSource = _transitionPending ? _selection : null;
            _transitionPending = false;
        }

        if (transitionSource is not null)
        {
            ScheduleFollowUpRefresh(generation);
            return SessionObservation.Create(transitionSource, PlaybackState.Unavailable);
        }

        if (provider is null)
        {
            return SessionObservation.Create(null, PlaybackState.Unavailable);
        }

        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        lock (_gate)
        {
            if (_selectionGeneration != generation
                || !ReferenceEquals(_activeProvider, provider))
            {
                return CreateCurrentUnavailableObservation();
            }

            _activeReadCancellation = readCancellation;
        }

        SessionObservation observation;
        try
        {
            observation = await provider.ReadAsync(readCancellation.Token);
        }
        catch (OperationCanceledException) when (readCancellation.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
            {
                throw;
            }

            return CreateCurrentUnavailableObservation();
        }
        catch (Exception) when (HasSelectionChanged(provider, generation))
        {
            return CreateCurrentUnavailableObservation();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeReadCancellation, readCancellation))
                {
                    _activeReadCancellation = null;
                }
            }
        }

        return HasSelectionChanged(provider, generation)
            ? CreateCurrentUnavailableObservation()
            : observation;
    }

    public async ValueTask DisposeAsync()
    {
        IMediaSourceProvider[] providers;
        Task[] followUps;
        CancellationTokenSource? activeReadCancellation;
        lock (_gate)
        {
            if (_disposeStarted)
            {
                return;
            }

            _disposeStarted = true;
            _disposed = true;
            activeReadCancellation = _activeReadCancellation;
            _selectionGeneration++;
            if (_activeProvider is not null)
            {
                _activeProvider.Changed -= OnActiveProviderChanged;
            }

            _activeProvider = null;
            _selection = null;
            providers = _providers.Values.ToArray();
            followUps = _followUpRefreshes.ToArray();
        }

        Exception? firstError = null;
        firstError = Cancel(_shutdown, firstError);
        firstError = Cancel(activeReadCancellation, firstError);
        foreach (var provider in providers)
        {
            try
            {
                await provider.DisposeAsync();
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
        }

        try
        {
            await Task.WhenAll(followUps.Select(IgnoreCancellationAsync));
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        try
        {
            _shutdown.Dispose();
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private void Select(SourceDescriptor? selection, bool notify)
    {
        IMediaSourceProvider? nextProvider = null;
        if (selection is not null && !_providers.TryGetValue(selection.Key.Provider, out nextProvider))
        {
            throw new ArgumentException(
                $"No media source provider is registered for {selection.Key.Provider}.",
                nameof(selection));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Equals(_selection?.Key, selection?.Key))
            {
                return;
            }

            var previousProvider = _activeProvider;
            if (previousProvider is not null && !ReferenceEquals(previousProvider, nextProvider))
            {
                previousProvider.Changed -= OnActiveProviderChanged;
                previousProvider.SetSelection(null);
            }

            _selectionGeneration = checked(_selectionGeneration + 1);
            _activeReadCancellation?.Cancel();
            _selection = selection;
            _activeProvider = nextProvider;
            _transitionPending = selection is not null;

            if (nextProvider is not null)
            {
                nextProvider.SetSelection(selection);
                if (!ReferenceEquals(previousProvider, nextProvider))
                {
                    nextProvider.Changed += OnActiveProviderChanged;
                }
            }
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActiveProviderChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _activeProvider))
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool HasSelectionChanged(IMediaSourceProvider provider, long generation)
    {
        lock (_gate)
        {
            return _selectionGeneration != generation
                || !ReferenceEquals(_activeProvider, provider);
        }
    }

    private SessionObservation CreateCurrentUnavailableObservation()
    {
        lock (_gate)
        {
            return SessionObservation.Create(_selection, PlaybackState.Unavailable);
        }
    }

    private void ScheduleFollowUpRefresh(long generation)
    {
        var task = FollowUpRefreshAsync(generation, _shutdown.Token);
        lock (_gate)
        {
            _followUpRefreshes.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    _followUpRefreshes.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task FollowUpRefreshAsync(long generation, CancellationToken cancellationToken)
    {
        // There is no clear-commit acknowledgement yet. This delay is generation/shutdown bounded;
        // remove it only when the coordinator exposes such an acknowledgement.
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        lock (_gate)
        {
            if (_disposed || _selectionGeneration != generation)
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Exception? Cancel(
        CancellationTokenSource? cancellation,
        Exception? firstError)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        return firstError;
    }
}
