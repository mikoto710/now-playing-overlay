using NowPlayingOverlay.Host.Media;

namespace NowPlayingOverlay.Host.Media.Development;

internal sealed class FakeSessionSource : ISessionSource, ISessionSourceStatus
{
    private readonly object _gate = new();
    private SessionObservation _current = SessionObservation.Create(null, Models.PlaybackState.Unavailable);
    private Exception? _readError;
    private bool _disposed;

    public event EventHandler? Changed;

    public bool IsAvailable => true;

    public void Publish(SessionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current = observation;
            _readError = null;
        }

        // Invoke callbacks outside the lock so scripted readers cannot deadlock publication.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PublishError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _readError = error;
        }

        // Match platform sources: event callbacks only request a later read.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RunScriptAsync(
        IEnumerable<FakeSessionStep> steps,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var clock = timeProvider ?? TimeProvider.System;
        foreach (var step in steps)
        {
            await Task.Delay(step.Delay, clock, cancellationToken);
            Publish(step.Observation);
        }
    }

    public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _readError is null
                ? ValueTask.FromResult(_current)
                : ValueTask.FromException<SessionObservation>(_readError);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
