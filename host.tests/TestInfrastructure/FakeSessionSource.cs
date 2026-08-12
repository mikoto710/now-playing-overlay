using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.TestInfrastructure;

internal sealed class FakeSessionSource : ISessionSource, ISessionSourceStatus
{
    private readonly object _gate = new();
    private SessionObservation _current = SessionObservation.Create(null, PlaybackState.Unavailable);
    private SourceManagerState _state = SourceManagerState.Unconfigured;
    private Exception? _readError;
    private bool _disposed;

    public event EventHandler? Changed;

    public SourceManagerState GetState()
    {
        lock (_gate)
        {
            return _state;
        }
    }

    public void Publish(SessionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current = observation;
            _readError = null;
            _state = observation.Source is null
                ? SourceManagerState.Unconfigured
                : new SourceManagerState(
                    observation.Source,
                    observation.Playback == PlaybackState.Unavailable
                        ? SourceStatus.Unavailable
                        : SourceStatus.Available,
                    observation.Playback == PlaybackState.Unavailable
                        ? SourceStatusReason.Missing
                        : SourceStatusReason.None);
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
            _state = new SourceManagerState(
                _current.Source,
                SourceStatus.Faulted,
                SourceStatusReason.Faulted);
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
