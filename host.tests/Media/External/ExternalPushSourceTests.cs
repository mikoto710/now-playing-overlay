using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.External;

public sealed class ExternalPushSourceTests
{
    private static readonly Guid ProducerId =
        Guid.Parse("b7f24a32-f0d6-465c-ac31-5d14f684c1b0");

    [Fact]
    public async Task FixedSelectionMapsLeaseStateAndArtworkWithoutTimeline()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromSeconds(10));
        await using var source = new ExternalPushSource(lease, NeverDelayAsync);
        source.SetSelection(SourceDescriptor.ExternalPush());

        lease.ApplyState(ExternalIngestState.Create(
            ProducerId,
            producerRevision: 1,
            PlaybackState.Playing,
            title: "Track",
            artist: "Artist",
            albumTitle: "Album",
            trackId: "track-1"));
        var artwork = ArtworkPayload.Create([1, 2, 3]);
        lease.ApplyArtwork(ProducerId, producerRevision: 1, artwork);

        var observation = await source.ReadAsync(CancellationToken.None);

        Assert.Equal(SourceProvider.ExternalPush, source.Provider);
        Assert.Equal(SourceKey.ExternalPush(), observation.Source!.Key);
        Assert.Equal(PlaybackState.Playing, observation.Playback);
        Assert.Equal("Track", observation.Track!.Title);
        Assert.Equal("track-1", observation.Identity!.ProviderTrackId);
        Assert.Null(observation.Timeline);
        Assert.Same(
            artwork,
            await observation.ArtworkReader!.ReadAsync(CancellationToken.None));
        Assert.Equal(SourceStatus.Available, source.GetState().Status);
    }

    [Fact]
    public async Task ExpiryMonitorPublishesUnavailableAndRaisesAChange()
    {
        var clock = new ManualTimeProvider();
        var lease = new ExternalProducerLease(TimeSpan.FromSeconds(10), clock);
        var delay = new StepDelay();
        await using var source = new ExternalPushSource(lease, delay.DelayAsync);
        source.SetSelection(SourceDescriptor.ExternalPush());
        lease.ApplyState(ExternalIngestState.Create(
            ProducerId,
            producerRevision: 1,
            PlaybackState.Paused,
            title: "Track"));
        var changes = 0;
        source.Changed += (_, _) => changes++;
        clock.Advance(TimeSpan.FromSeconds(10));

        delay.Release();
        await WaitUntilAsync(() => Volatile.Read(ref changes) == 1);

        Assert.Equal(SourceStatus.Unavailable, source.GetState().Status);
        Assert.Equal(SourceStatusReason.Missing, source.GetState().Reason);
        Assert.Equal(
            PlaybackState.Unavailable,
            (await source.ReadAsync(CancellationToken.None)).Playback);
    }

    [Fact]
    public async Task RejectsDescriptorsFromOtherProviders()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromSeconds(10));
        await using var source = new ExternalPushSource(lease, NeverDelayAsync);

        Assert.Throws<ArgumentException>(
            () => source.SetSelection(SourceDescriptor.WindowsMedia("Player.App")));
    }

    private static ValueTask NeverDelayAsync(TimeSpan _, CancellationToken cancellationToken)
    {
        return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StepDelay
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(250), duration);
            return Interlocked.Increment(ref _calls) == 1
                ? new ValueTask(_gate.Task.WaitAsync(cancellationToken))
                : new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        }

        public void Release()
        {
            _gate.TrySetResult();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _timestamp = checked(_timestamp + duration.Ticks);
        }
    }
}
