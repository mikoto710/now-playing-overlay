using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.External;

public sealed class ExternalProducerLeaseTests
{
    private static readonly Guid FirstProducer = Guid.Parse("6ec6ece6-6ee1-49dd-8819-22ef82010342");
    private static readonly Guid SecondProducer = Guid.Parse("710871aa-33fb-485c-96a9-29885ee1960c");

    [Fact]
    public void FirstStateClaimsLeaseAndIncreasingRevisionUpdatesIt()
    {
        var lease = CreateLease(out _);

        var first = lease.ApplyState(Playing(FirstProducer, 1, "First"));
        var second = lease.ApplyState(Playing(FirstProducer, 2, "Second"));

        Assert.Equal(ExternalLeaseStateResult.Accepted, first);
        Assert.Equal(ExternalLeaseStateResult.Accepted, second);
        Assert.Equal(2, lease.GetCurrentState()!.ProducerRevision);
        Assert.Equal("Second", lease.GetCurrentState()!.Track!.Title);
    }

    [Fact]
    public void StaleStateIsRejectedAndDoesNotRenewLease()
    {
        var lease = CreateLease(out var clock);
        Assert.Equal(
            ExternalLeaseStateResult.Accepted,
            lease.ApplyState(Playing(FirstProducer, 2, "Current")));
        clock.Advance(TimeSpan.FromSeconds(9));

        var stale = lease.ApplyState(Playing(FirstProducer, 2, "Replay"));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ExternalLeaseStateResult.StaleRevision, stale);
        Assert.True(lease.TryExpire());
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public void ForeignStateAndHeartbeatCannotReplaceOrRenewOwner()
    {
        var lease = CreateLease(out var clock);
        lease.ApplyState(Playing(FirstProducer, 1, "Owner"));
        clock.Advance(TimeSpan.FromSeconds(9));

        var stateResult = lease.ApplyState(Playing(SecondProducer, 1, "Foreign"));
        var heartbeatResult = lease.Heartbeat(SecondProducer);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ExternalLeaseStateResult.ProducerConflict, stateResult);
        Assert.Equal(ExternalLeaseHeartbeatResult.ProducerConflict, heartbeatResult);
        Assert.True(lease.TryExpire());
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public void OwnerHeartbeatRenewsWithoutChangingPublishedState()
    {
        var lease = CreateLease(out var clock);
        var state = Playing(FirstProducer, 1, "Owner");
        lease.ApplyState(state);
        clock.Advance(TimeSpan.FromSeconds(9));

        var result = lease.Heartbeat(FirstProducer);
        clock.Advance(TimeSpan.FromSeconds(9));

        Assert.Equal(ExternalLeaseHeartbeatResult.Renewed, result);
        Assert.False(lease.TryExpire());
        Assert.Same(state, lease.GetCurrentState());
    }

    [Fact]
    public void ArtworkRequiresTheCurrentOwnerRevisionAndDoesNotRenewLease()
    {
        var lease = CreateLease(out var clock);
        lease.ApplyState(Playing(FirstProducer, 4, "Owner"));
        clock.Advance(TimeSpan.FromSeconds(9));
        var artwork = ArtworkPayload.Create([1, 2, 3]);

        var foreign = lease.ApplyArtwork(SecondProducer, 4, artwork);
        var stale = lease.ApplyArtwork(FirstProducer, 3, artwork);
        var accepted = lease.ApplyArtwork(FirstProducer, 4, artwork);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ExternalLeaseArtworkResult.ProducerConflict, foreign);
        Assert.Equal(ExternalLeaseArtworkResult.RevisionConflict, stale);
        Assert.Equal(ExternalLeaseArtworkResult.Accepted, accepted);
        Assert.True(lease.TryExpire());
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public void ArtworkSurvivesPlaybackUpdatesButNotAChangedTrackIdentity()
    {
        var lease = CreateLease(out _);
        lease.ApplyState(Playing(FirstProducer, 1, "Track"));
        var artwork = ArtworkPayload.Create([1, 2, 3]);
        Assert.Equal(
            ExternalLeaseArtworkResult.Accepted,
            lease.ApplyArtwork(FirstProducer, 1, artwork));

        lease.ApplyState(ExternalIngestState.Create(
            FirstProducer,
            2,
            PlaybackState.Paused,
            "Track",
            "Artist"));
        var paused = lease.GetCurrentState()!;
        lease.ApplyState(Playing(FirstProducer, 3, "Replacement"));

        Assert.Same(artwork, paused.Artwork);
        Assert.Null(lease.GetCurrentState()!.Artwork);
    }

    [Fact]
    public void ArtworkRejectsMissingLeaseAndAStateWithoutTrack()
    {
        var lease = CreateLease(out _);
        var artwork = ArtworkPayload.Create([1, 2, 3]);

        var missing = lease.ApplyArtwork(FirstProducer, 1, artwork);
        lease.ApplyState(ExternalIngestState.Create(
            FirstProducer,
            1,
            PlaybackState.Idle));
        var trackless = lease.ApplyArtwork(FirstProducer, 1, artwork);

        Assert.Equal(ExternalLeaseArtworkResult.NoActiveLease, missing);
        Assert.Equal(ExternalLeaseArtworkResult.MissingTrack, trackless);
    }

    [Fact]
    public void ExpiryClearsOwnerAndAllowsAnotherProducerToClaim()
    {
        var lease = CreateLease(out var clock);
        lease.ApplyState(Playing(FirstProducer, 8, "First"));
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(
            ExternalLeaseHeartbeatResult.NoActiveLease,
            lease.Heartbeat(SecondProducer));
        Assert.Equal(
            ExternalLeaseStateResult.Accepted,
            lease.ApplyState(Playing(SecondProducer, 1, "Second")));
        Assert.Equal(SecondProducer, lease.GetCurrentState()!.ProducerId);
    }

    [Fact]
    public void CurrentStateReadExpiresLeaseAtTheExactBoundary()
    {
        var lease = CreateLease(out var clock);
        lease.ApplyState(Playing(FirstProducer, 1, "Owner"));
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(lease.GetCurrentState());
        Assert.False(lease.TryExpire());
    }

    [Fact]
    public void ConstructorAndHeartbeatRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExternalProducerLease(TimeSpan.Zero));
        var lease = CreateLease(out _);
        Assert.Throws<ArgumentException>(() => lease.Heartbeat(Guid.Empty));
    }

    [Fact]
    public void ChangeSignalCoversAcceptedStateAndExpiryButNotHeartbeatOrRejection()
    {
        var lease = CreateLease(out var clock);
        var changes = 0;
        lease.StateChanged += (_, _) => changes++;

        lease.ApplyState(Playing(FirstProducer, 1, "Owner"));
        lease.Heartbeat(FirstProducer);
        lease.ApplyArtwork(FirstProducer, 1, ArtworkPayload.Create([1, 2, 3]));
        lease.ApplyState(Playing(FirstProducer, 1, "Replay"));
        lease.ApplyState(Playing(SecondProducer, 2, "Foreign"));
        clock.Advance(TimeSpan.FromSeconds(10));
        lease.TryExpire();

        Assert.Equal(3, changes);
    }

    [Fact]
    public void HostRevocationClearsOwnerImmediatelyAndIsIdempotent()
    {
        var lease = CreateLease(out _);
        lease.ApplyState(Playing(FirstProducer, 1, "Owner"));
        var changes = 0;
        lease.StateChanged += (_, _) => changes++;

        var revoked = lease.Revoke();
        var repeated = lease.Revoke();

        Assert.True(revoked);
        Assert.False(repeated);
        Assert.Null(lease.GetCurrentState());
        Assert.Equal(1, changes);
        Assert.Equal(
            ExternalLeaseStateResult.Accepted,
            lease.ApplyState(Playing(SecondProducer, 1, "Replacement")));
    }

    private static ExternalProducerLease CreateLease(out ManualTimeProvider clock)
    {
        clock = new ManualTimeProvider();
        return new ExternalProducerLease(TimeSpan.FromSeconds(10), clock);
    }

    private static ExternalIngestState Playing(Guid producerId, long revision, string title)
    {
        return ExternalIngestState.Create(
            producerId,
            revision,
            PlaybackState.Playing,
            title,
            "Artist");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return _timestamp;
        }

        public void Advance(TimeSpan duration)
        {
            _timestamp = checked(_timestamp + duration.Ticks);
        }
    }
}
