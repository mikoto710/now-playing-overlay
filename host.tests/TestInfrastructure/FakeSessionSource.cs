using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.TestInfrastructure;

internal sealed class FakeSessionSource : ISessionSource, ISessionSourceStatus
{
    private readonly object _gate = new();
    private SessionObservation _current = SessionObservation.Create(null, PlaybackState.Unavailable);
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

        Changed?.Invoke(this, EventArgs.Empty);
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
