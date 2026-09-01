using System.Net;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed partial class OverlayHttpTests
{
    [Fact]
    public async Task SpotifyCallbackUsesTheHostPortOnlyDuringPendingAuthorization()
    {
        var port = ReservePort();
        var source = new FakeSessionSource();
        var callbackBroker = new SpotifyAuthorizationCallbackBroker();
        var graph = OverlayCompositionRoot.BuildRuntime(
            new HostOptions { Port = port },
            source,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            callbackBroker);
        await using var app = graph.Runtime;
        await app.StartAsync();
        using var client = CreateClient(port);

        using var inactive = await client.GetAsync(SpotifyAuthorizationRequest.RedirectPath);
        using var registration = callbackBroker.Begin("expected-state");
        using var active = await client.GetAsync(
            $"{SpotifyAuthorizationRequest.RedirectPath}?code=authorization-code&state=expected-state");
        var code = await registration.WaitForAuthorizationCodeAsync(
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Equal("authorization-code", code);
    }
}
