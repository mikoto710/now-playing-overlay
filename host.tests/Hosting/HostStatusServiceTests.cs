using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Development;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class HostStatusServiceTests
{
    [Fact]
    public async Task ReportsLifecycleWithoutExposingTrackMetadata()
    {
        var source = new FakeSessionSource();
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
        var coordinator = new NowPlayingCoordinator(source, store, new ArtworkCache());
        var runtime = new HostRuntimeState(TimeProvider.System);
        var service = new HostStatusService(runtime, coordinator, source, store);

        Assert.Equal("Host Starting", service.GetCurrent().Text);
        runtime.MarkReady();
        Assert.Equal("Waiting for Spotify", service.GetCurrent().Text);
        store.TryCommit(
            "Spotify.exe",
            PlaybackState.Playing,
            TrackMetadata.Create("Private track title", "Private artist", albumTitle: null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);

        var status = service.GetCurrent();

        Assert.Equal("Spotify: Playing", status.Text);
        Assert.DoesNotContain("Private", status.Text, StringComparison.Ordinal);
        await coordinator.DisposeAsync();
    }
}
