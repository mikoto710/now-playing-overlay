using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Tests.State;

public sealed class NowPlayingStoreTests
{
    private static readonly Guid ServerInstanceId = Guid.Parse("39c84f34-d588-4ae4-9fe7-043fa77a560e");

    [Fact]
    public void TryCommitIncrementsRevisionOnlyForVisibleChanges()
    {
        var store = CreateStore(ServerInstanceId);
        var track = TrackMetadata.Create("Title", "Artist", null);

        Assert.True(
            store.TryCommit(
                SourceDescriptor.WindowsMedia("Player.App"),
                PlaybackState.Playing,
                track,
                artwork: null,
                DateTimeOffset.UtcNow,
                out var first));
        Assert.False(
            store.TryCommit(
                SourceDescriptor.WindowsMedia("Player.App"),
                PlaybackState.Playing,
                TrackMetadata.Create("Title", "Artist", null),
                artwork: null,
                DateTimeOffset.UtcNow.AddMinutes(1),
                out var duplicate));

        Assert.Equal(1, first.SnapshotRevision);
        Assert.Same(first, duplicate);
        Assert.Equal(1, store.Current.SnapshotRevision);
    }

    [Fact]
    public void TryCommitUsesPlayingTimelineSemanticsForRevision()
    {
        var sampledAt = new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);
        var store = CreateStore(ServerInstanceId);
        var source = SourceDescriptor.WindowsMedia("Player.App");
        var track = TrackMetadata.Create("Title", "Artist", null);
        var initialTimeline = PlaybackTimeline.Create(1_000, 240_000, sampledAt);

        Assert.True(
            store.TryCommit(
                source,
                PlaybackState.Playing,
                track,
                initialTimeline,
                artwork: null,
                sampledAt,
                out var first));
        Assert.False(
            store.TryCommit(
                source,
                PlaybackState.Playing,
                track,
                PlaybackTimeline.Create(3_500, 240_000, sampledAt.AddSeconds(2)),
                artwork: null,
                sampledAt.AddSeconds(2),
                out var equivalent));
        Assert.True(
            store.TryCommit(
                source,
                PlaybackState.Playing,
                track,
                PlaybackTimeline.Create(3_501, 240_000, sampledAt.AddSeconds(2)),
                artwork: null,
                sampledAt.AddSeconds(2),
                out var corrected));

        Assert.Equal(1, first.SnapshotRevision);
        Assert.Same(first, equivalent);
        Assert.Same(initialTimeline, equivalent.Timeline);
        Assert.Equal(2, corrected.SnapshotRevision);
    }

    [Fact]
    public void TryCommitCreatesRevisionsForPausedMovementAndNullTransition()
    {
        var sampledAt = new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);
        var store = CreateStore(ServerInstanceId);
        var source = SourceDescriptor.WindowsMedia("Player.App");
        var track = TrackMetadata.Create("Title", "Artist", null);

        Assert.True(
            store.TryCommit(
                source,
                PlaybackState.Paused,
                track,
                PlaybackTimeline.Create(1_000, 240_000, sampledAt),
                artwork: null,
                sampledAt,
                out var first));
        Assert.False(
            store.TryCommit(
                source,
                PlaybackState.Paused,
                track,
                PlaybackTimeline.Create(1_000, 240_000, sampledAt.AddMinutes(1)),
                artwork: null,
                sampledAt.AddMinutes(1),
                out var resampled));
        Assert.True(
            store.TryCommit(
                source,
                PlaybackState.Paused,
                track,
                PlaybackTimeline.Create(1_001, 240_000, sampledAt.AddMinutes(1)),
                artwork: null,
                sampledAt.AddMinutes(1),
                out var moved));
        Assert.True(
            store.TryCommit(
                source,
                PlaybackState.Paused,
                track,
                timeline: null,
                artwork: null,
                sampledAt.AddMinutes(2),
                out var removed));

        Assert.Equal(1, first.SnapshotRevision);
        Assert.Same(first, resampled);
        Assert.Equal(2, moved.SnapshotRevision);
        Assert.Equal(3, removed.SnapshotRevision);
        Assert.Null(removed.Timeline);
    }

    [Fact]
    public void TryCommitDoesNotCarryTimelineToDifferentTrack()
    {
        var store = CreateStore(ServerInstanceId);
        var source = SourceDescriptor.WindowsMedia("Player.App");
        store.TryCommit(
            source,
            PlaybackState.Playing,
            TrackMetadata.Create("A", "Artist", null),
            PlaybackTimeline.Create(1_000, 240_000, DateTimeOffset.UtcNow),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);

        store.TryCommit(
            source,
            PlaybackState.Playing,
            TrackMetadata.Create("B", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out var changedTrack);

        Assert.Equal("B", changedTrack.Track!.Title);
        Assert.Null(changedTrack.Timeline);
    }

    [Fact]
    public async Task SubscriptionStartsCurrentAndDropsIntermediateSnapshots()
    {
        var store = CreateStore(ServerInstanceId);
        using var subscription = store.Subscribe();

        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("A", "Artist", null),
            null,
            DateTimeOffset.UtcNow,
            out _);
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("B", "Artist", null),
            null,
            DateTimeOffset.UtcNow,
            out var latest);

        var received = await subscription.Reader.ReadAsync();

        Assert.Same(latest, received);
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public async Task OrderedSubscriptionPreservesEveryCommittedSnapshot()
    {
        var store = CreateStore(ServerInstanceId);
        using var subscription = store.SubscribeOrdered(capacity: 4);
        var initial = await subscription.Reader.ReadAsync();
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("A", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out var first);
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("B", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out var second);

        Assert.Equal(0, initial.SnapshotRevision);
        Assert.Same(first, await subscription.Reader.ReadAsync());
        Assert.Same(second, await subscription.Reader.ReadAsync());
    }

    [Fact]
    public async Task OrderedSubscriptionFaultsInsteadOfSilentlyDroppingOverflow()
    {
        var store = CreateStore(ServerInstanceId);
        using var subscription = store.SubscribeOrdered(capacity: 1);

        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("A", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);

        Assert.Equal(0, (await subscription.Reader.ReadAsync()).SnapshotRevision);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await subscription.Reader.Completion);
    }

    [Fact]
    public void NewServerInstanceCanStartFromRevisionZero()
    {
        var firstStore = CreateStore(ServerInstanceId);
        firstStore.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("Title", "Artist", null),
            null,
            DateTimeOffset.UtcNow,
            out _);

        var secondStore = CreateStore(Guid.NewGuid());

        Assert.Equal(1, firstStore.Current.SnapshotRevision);
        Assert.Equal(0, secondStore.Current.SnapshotRevision);
        Assert.NotEqual(firstStore.Current.ServerInstanceId, secondStore.Current.ServerInstanceId);
    }

    [Fact]
    public void ConstructorRejectsNonInitialRevision()
    {
        var snapshot = NowPlayingSnapshot.Create(
            ServerInstanceId,
            1,
            null,
            PlaybackState.Unavailable,
            null,
            null,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => new NowPlayingStore(snapshot));
    }

    private static NowPlayingStore CreateStore(Guid serverInstanceId)
    {
        return new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(serverInstanceId, DateTimeOffset.UtcNow));
    }
}
