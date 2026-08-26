using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

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
        Assert.Equal("Source Not Configured", service.GetCurrent().Text);
        source.Publish(SessionObservation.Create(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("Private track title", "Private artist", albumTitle: null)));
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("Private track title", "Private artist", albumTitle: null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);

        var status = service.GetCurrent();

        Assert.Equal("Windows Media: Playing", status.Text);
        Assert.DoesNotContain("Spotify", status.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", status.Text, StringComparison.Ordinal);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task BrowserPlayerMissingStatusExplainsTheUserAction()
    {
        var source = new FakeSessionSource();
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
        var coordinator = new NowPlayingCoordinator(source, store, new ArtworkCache());
        var runtime = new HostRuntimeState(TimeProvider.System);
        runtime.MarkReady();
        source.Publish(SessionObservation.Create(
            SourceDescriptor.ExternalPush(),
            PlaybackState.Unavailable));
        var service = new HostStatusService(runtime, coordinator, source, store);

        Assert.Equal(
            "Browser Player: Waiting for Browser Producer",
            service.GetCurrent().Text);

        await coordinator.DisposeAsync();
    }
}
