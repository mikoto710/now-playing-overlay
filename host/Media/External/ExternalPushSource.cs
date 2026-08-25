using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.External;

internal sealed class ExternalPushSource : IMediaSourceProvider
{
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultExpiryCheckInterval = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private readonly ExternalProducerLease _lease;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delay;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _expiryMonitor;
    private SourceDescriptor? _selection;
    private bool _disposed;

    public ExternalPushSource(ExternalProducerLease lease)
        : this(lease, DefaultDelayAsync)
    {
    }

    internal ExternalPushSource(
        ExternalProducerLease lease,
        Func<TimeSpan, CancellationToken, ValueTask> delay)
    {
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _lease.StateChanged += OnLeaseStateChanged;
        _expiryMonitor = MonitorExpiryAsync(_shutdown.Token);
    }

    public event EventHandler? Changed;

    public SourceProvider Provider => SourceProvider.ExternalPush;

    public SourceManagerState GetState()
    {
        var selection = GetSelection();
        if (selection is null)
        {
            return SourceManagerState.Unconfigured;
        }

        return _lease.GetCurrentState() is null
            ? new SourceManagerState(selection, SourceStatus.Unavailable, SourceStatusReason.Missing)
            : new SourceManagerState(selection, SourceStatus.Available, SourceStatusReason.None);
    }

    public void SetSelection(SourceDescriptor? selection)
    {
        ValidateSelection(selection);
        var changed = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Equals(_selection?.Key, selection?.Key))
            {
                _selection = selection;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = GetSelection();
        if (selection is null)
        {
            return ValueTask.FromResult(
                SessionObservation.Create(null, PlaybackState.Unavailable));
        }

        var state = _lease.GetCurrentState();
        return ValueTask.FromResult(state is null
            ? SessionObservation.Create(selection, PlaybackState.Unavailable)
            : SessionObservation.Create(selection, state.Playback, state.Track));
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _selection = null;
            _shutdown.Cancel();
        }

        _lease.StateChanged -= OnLeaseStateChanged;
        try
        {
            await _expiryMonitor;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }

        _shutdown.Dispose();
    }

    private SourceDescriptor? GetSelection()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _selection;
        }
    }

    private async Task MonitorExpiryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _delay(DefaultExpiryCheckInterval, cancellationToken);
            _lease.TryExpire();
        }
    }

    private void OnLeaseStateChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || _selection is null)
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateSelection(SourceDescriptor? selection)
    {
        if (selection is not null && selection.Key != SourceKey.ExternalPush())
        {
            throw new ArgumentException(
                "External Push only accepts the fixed Custom Source descriptor.",
                nameof(selection));
        }
    }

    private static async ValueTask DefaultDelayAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await Task.Delay(duration, cancellationToken);
    }
}
