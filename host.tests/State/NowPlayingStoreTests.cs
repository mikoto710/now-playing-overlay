using NowPlayingOverlay.Host.Media;
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
