using NowPlayingOverlay.Host.Artwork;

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

internal enum ExternalLeaseArtworkResult
{
    Accepted,
    NoActiveLease,
    ProducerConflict,
    RevisionConflict,
    MissingTrack,
}

/// <summary>
/// Owns the single-producer Browser Player lease and its authoritative revisioned state.
/// </summary>
/// <remarks>
/// With no active lease, the first valid state claims it. The same producer must increase revision;
/// another producer conflicts until expiry or revocation. Heartbeats renew only the owner. Artwork
/// commits require the current producer, exact revision, and a track, so slow uploads are rejected
/// after state advances. Expiry and key-rotation revocation clear the full state. The gate protects
/// lease state; StateChanged is a reread signal and is raised outside it.
/// </remarks>
internal sealed class ExternalProducerLease
{
    // Protects producer identity, revision, artwork binding, and renewal time.
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
                // Playback-only updates retain artwork; a new media identity clears it immediately.
                _state = Equals(_state.Identity, state.Identity)
                    ? state.WithArtwork(_state.Artwork)
                    : state;
                _renewedAtTimestamp = now;
                result = ExternalLeaseStateResult.Accepted;
                changed = true;
            }
        }

        NotifyStateChanged(changed);
        return result;
    }

    public ExternalLeaseArtworkResult ApplyArtwork(
        Guid producerId,
        long producerRevision,
        ArtworkPayload artwork)
    {
        ValidateArtworkTarget(producerId, producerRevision);
        ArgumentNullException.ThrowIfNull(artwork);
        ExternalLeaseArtworkResult result;
        var changed = false;
        lock (_gate)
        {
            changed = ExpireLocked(_timeProvider.GetTimestamp());
            result = CheckArtworkTargetLocked(producerId, producerRevision);
            if (result == ExternalLeaseArtworkResult.Accepted)
            {
                _state = _state!.WithArtwork(artwork);
                changed = true;
            }
        }

        NotifyStateChanged(changed);
        return result;
    }

    public ExternalLeaseArtworkResult CheckArtworkTarget(Guid producerId, long producerRevision)
    {
        ValidateArtworkTarget(producerId, producerRevision);
        ExternalLeaseArtworkResult result;
        bool expired;
        lock (_gate)
        {
            expired = ExpireLocked(_timeProvider.GetTimestamp());
            result = CheckArtworkTargetLocked(producerId, producerRevision);
        }

        NotifyStateChanged(expired);
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

    private ExternalLeaseArtworkResult CheckArtworkTargetLocked(
        Guid producerId,
        long producerRevision)
    {
        if (_state is null)
        {
            return ExternalLeaseArtworkResult.NoActiveLease;
        }

        if (_state.ProducerId != producerId)
        {
            return ExternalLeaseArtworkResult.ProducerConflict;
        }

        if (_state.ProducerRevision != producerRevision)
        {
            return ExternalLeaseArtworkResult.RevisionConflict;
        }

        return _state.Track is null
            ? ExternalLeaseArtworkResult.MissingTrack
            : ExternalLeaseArtworkResult.Accepted;
    }

    private static void ValidateArtworkTarget(Guid producerId, long producerRevision)
    {
        if (producerId == Guid.Empty)
        {
            throw new ArgumentException("Producer ID must not be empty.", nameof(producerId));
        }

        if (producerRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(producerRevision),
                producerRevision,
                "Producer revision must be positive.");
        }
    }

    private void NotifyStateChanged(bool changed)
    {
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
