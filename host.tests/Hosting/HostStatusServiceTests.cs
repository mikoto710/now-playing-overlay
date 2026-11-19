using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;
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

    [Theory]
    [InlineData(false, "Spotify API: Rate Limited")]
    [InlineData(true, "Spotify API: Quota Exceeded")]
    public async Task Spotify429StatusDistinguishesConfirmedQuotaFromUnconfirmedRateLimits(bool quota, string expected)
    {
        using var http = new HttpClient();
        var provider = new SpotifyApiSource((_, _) => Task.FromException<SpotifyPlaybackResult>(
            new SpotifyRateLimitException(new SpotifyCooldown("connection", SpotifyEndpoint.CurrentlyPlaying,
                quota, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15), 1), TimeSpan.FromMinutes(15))),
            new SpotifyClientId("client-id"), http, TimeProvider.System,
            (delay, cancellationToken) => new ValueTask(Task.Delay(delay, cancellationToken)));
        await using var source = new ActiveSourceManager([provider], SourceDescriptor.SpotifyApi());
        var store = new NowPlayingStore(NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
        await using var coordinator = new NowPlayingCoordinator(source, store, new ArtworkCache());
        var runtime = new HostRuntimeState(TimeProvider.System);
        runtime.MarkReady();
        var service = new HostStatusService(runtime, coordinator, source, store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (source.GetState().Status != SourceStatus.Unavailable)
        {
            await Task.Delay(1, timeout.Token);
        }

        Assert.Equal(expected, service.GetCurrent().Text);
        Assert.False(service.GetCurrent().IsFaulted);
    }
}
