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

    public event EventHandler? StateChanged;

    internal TimeSpan LeaseDuration => _leaseDuration;

    public ExternalIngestState? GetCurrentState()
    {
        ExternalIngestState? state;
        bool expired;
        lock (_gate)
        {
            expired = ExpireLocked(_timeProvider.GetTimestamp());
            state = _state;
        }

        NotifyStateChanged(expired);
        return state;
    }

    public ExternalLeaseStateResult ApplyState(ExternalIngestState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ExternalLeaseStateResult result;
        var changed = false;
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            ExpireLocked(now);
            if (_state is null)
            {
                _state = state;
                _renewedAtTimestamp = now;
                result = ExternalLeaseStateResult.Accepted;
                changed = true;
            }
            else if (_state.ProducerId != state.ProducerId)
            {
                result = ExternalLeaseStateResult.ProducerConflict;
            }
            else if (state.ProducerRevision <= _state.ProducerRevision)
            {
                result = ExternalLeaseStateResult.StaleRevision;
            }
            else
            {
                _state = state;
                _renewedAtTimestamp = now;
                result = ExternalLeaseStateResult.Accepted;
                changed = true;
            }
        }

        NotifyStateChanged(changed);
        return result;
    }

    public ExternalLeaseHeartbeatResult Heartbeat(Guid producerId)
    {
        if (producerId == Guid.Empty)
        {
            throw new ArgumentException("Producer ID must not be empty.", nameof(producerId));
        }

        ExternalLeaseHeartbeatResult result;
        var expired = false;
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            expired = ExpireLocked(now);
            if (_state is null)
            {
                result = ExternalLeaseHeartbeatResult.NoActiveLease;
            }
            else if (_state.ProducerId != producerId)
            {
                result = ExternalLeaseHeartbeatResult.ProducerConflict;
            }
            else
            {
                _renewedAtTimestamp = now;
                result = ExternalLeaseHeartbeatResult.Renewed;
            }
        }

        NotifyStateChanged(expired);
        return result;
    }

    public bool TryExpire()
    {
        bool expired;
        lock (_gate)
        {
            expired = ExpireLocked(_timeProvider.GetTimestamp());
        }

        NotifyStateChanged(expired);
        return expired;
    }

    public bool Revoke()
    {
        bool revoked;
        lock (_gate)
        {
            revoked = _state is not null;
            _state = null;
            _renewedAtTimestamp = 0;
        }

        NotifyStateChanged(revoked);
        return revoked;
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

    private void NotifyStateChanged(bool changed)
    {
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
