namespace NowPlayingOverlay.Host.Media.External;

internal enum ExternalLeaseStateResult
{
    Accepted,
    StaleRevision,
    ProducerConflict,
}

internal enum ExternalLeaseHeartbeatResult
{
    Renewed,
    NoActiveLease,
    ProducerConflict,
}

internal sealed class ExternalProducerLease
{
    private readonly object _gate = new();
    private readonly TimeSpan _leaseDuration;
    private readonly TimeProvider _timeProvider;
    private ExternalIngestState? _state;
    private long _renewedAtTimestamp;

    public ExternalProducerLease(TimeSpan leaseDuration, TimeProvider? timeProvider = null)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        _leaseDuration = leaseDuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ExternalIngestState? GetCurrentState()
    {
        lock (_gate)
        {
            ExpireLocked(_timeProvider.GetTimestamp());
            return _state;
        }
    }

    public ExternalLeaseStateResult ApplyState(ExternalIngestState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            ExpireLocked(now);
            if (_state is null)
            {
                _state = state;
                _renewedAtTimestamp = now;
                return ExternalLeaseStateResult.Accepted;
            }

            if (_state.ProducerId != state.ProducerId)
            {
                return ExternalLeaseStateResult.ProducerConflict;
            }

            if (state.ProducerRevision <= _state.ProducerRevision)
            {
                return ExternalLeaseStateResult.StaleRevision;
            }

            _state = state;
            _renewedAtTimestamp = now;
            return ExternalLeaseStateResult.Accepted;
        }
    }

    public ExternalLeaseHeartbeatResult Heartbeat(Guid producerId)
    {
        if (producerId == Guid.Empty)
        {
            throw new ArgumentException("Producer ID must not be empty.", nameof(producerId));
        }

        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            ExpireLocked(now);
            if (_state is null)
            {
                return ExternalLeaseHeartbeatResult.NoActiveLease;
            }

            if (_state.ProducerId != producerId)
            {
                return ExternalLeaseHeartbeatResult.ProducerConflict;
            }

            _renewedAtTimestamp = now;
            return ExternalLeaseHeartbeatResult.Renewed;
        }
    }

    public bool TryExpire()
    {
        lock (_gate)
        {
            return ExpireLocked(_timeProvider.GetTimestamp());
        }
    }

    private bool ExpireLocked(long now)
    {
        if (_state is null
            || _timeProvider.GetElapsedTime(_renewedAtTimestamp, now) < _leaseDuration)
        {
            return false;
        }

        _state = null;
        _renewedAtTimestamp = 0;
        return true;
    }
}
