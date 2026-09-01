using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed partial class OverlayHttpTests
{
    [Fact]
    public async Task ServerBindsOnlyIpv4LoopbackAndFiltersHost()
    {
        await using var host = await TestOverlayHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "localhost";

        using var response = await host.Client.SendAsync(request);
        using var ipv6 = new TcpClient(AddressFamily.InterNetworkV6);
        await Assert.ThrowsAnyAsync<SocketException>(
            () => ipv6.ConnectAsync(IPAddress.IPv6Loopback, host.Port));

        Assert.Equal(host.Port, host.Runtime.CurrentPort);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesDoNotIdentifyTheServerAndUnknownRoutesFailClosed()
    {
        await using var host = await TestOverlayHost.StartAsync();

        using var health = await host.Client.GetAsync("/health");
        using var missing = await host.Client.GetAsync("/not-an-overlay-route");
        using var disabledIngest = await host.Client.PostAsync(
            ExternalIngestHttpHandler.StatePath,
            content: null);

        Assert.False(health.Headers.Contains("Server"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, disabledIngest.StatusCode);
        Assert.Equal("nosniff", missing.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task HeadReturnsEndpointHeadersWithoutBodiesAndUnsupportedMethodIsRejected()
    {
        await using var host = await TestOverlayHost.StartAsync();
        host.Source.Publish(Playing(
            "Artwork track",
            new ImmediateArtworkReader(ArtworkPayload.Create(OnePixelPng))));
        using var state = await host.WaitForArtworkAsync();
        var artworkUrl = state.RootElement.GetProperty("artwork").GetProperty("url").GetString()!;

        using var pageHead = await SendHeadAsync(host.Client, "/NowPlaying.html");
        using var stateHead = await SendHeadAsync(host.Client, "/api/v3/state");
        using var appearanceHead = await SendHeadAsync(host.Client, "/api/v3/appearance");
        using var artworkHead = await SendHeadAsync(host.Client, artworkUrl);
        using var healthHead = await SendHeadAsync(host.Client, "/health");
        using var post = await host.Client.PostAsync("/api/v3/state", content: null);

        AssertHead(pageHead, "text/html", "no-store");
        AssertHead(stateHead, "application/json", "no-store");
        AssertHead(appearanceHead, "application/json", "no-store");
        AssertHead(artworkHead, "image/png", "public, max-age=31536000, immutable");
        AssertHead(healthHead, "application/json", "no-store");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task RequestHeaderCountAndTotalSizeAreBounded()
    {
        await using var host = await TestOverlayHost.StartAsync();
        var manyHeaders = string.Concat(Enumerable.Range(0, 40).Select(index => $"X-M7-{index}: value\r\n"));
        var largeHeaders = $"X-M7-Large: {new string('a', 17 * 1024)}\r\n";

        var manyHeadersStatus = await SendRawRequestAsync(host.Port, manyHeaders);
        var largeHeadersStatus = await SendRawRequestAsync(host.Port, largeHeaders);

        Assert.Equal(431, manyHeadersStatus);
        // HTTP.sys rejects a single oversized field before HttpListener creates a context.
        // The maintainer accepted its native 400 response; application-level aggregate limits stay 431.
        Assert.Equal(400, largeHeadersStatus);
    }

    [Fact]
    public async Task GracefulStopClosesRuntimeAndDisposesSessionSource()
    {
        var port = ReservePort();
        var source = new FakeSessionSource();
        var graph = OverlayCompositionRoot.BuildRuntime(
            new HostOptions { Port = port },
            source,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly));
        var app = graph.Runtime;
        await app.StartAsync();

        await app.StopAsync();

        Assert.Throws<ObjectDisposedException>(() =>
            source.Publish(SessionObservation.Create(null, PlaybackState.Unavailable)));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task OccupiedPortFailsWithoutSelectingAnotherPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var graph = OverlayCompositionRoot.BuildRuntime(
            new HostOptions { Port = port },
            new FakeSessionSource(),
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly));
        var app = graph.Runtime;

        await Assert.ThrowsAnyAsync<Exception>(async () => await app.StartAsync());

        await app.DisposeAsync();
    }

    [Fact]
    public async Task LivePortRebindPublishesTheNewEndpointAndRetiresTheOldPort()
    {
        await using var host = await TestOverlayHost.StartAsync(rebindGraceMilliseconds: 300);
        using var events = await host.Client.GetAsync(
            "/api/v3/events",
            HttpCompletionOption.ResponseHeadersRead);
        await using var eventStream = await events.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(eventStream);
        _ = await ReadSseEventAsync(reader);
        var newPort = ReservePort();
        var persistedPort = 0;

        await host.HttpServer.RebindAsync(newPort, () => persistedPort = newPort);
        var endpointEvent = await ReadSseEventAsync(reader);
        using var endpointJson = JsonDocument.Parse(endpointEvent.Data);
        using var newClient = CreateClient(newPort);
        using var newHealth = await newClient.GetAsync("/health");
        using var oldDuringGrace = await host.Client.GetAsync("/health");

        Assert.Equal("server", endpointEvent.Event);
        Assert.Null(endpointEvent.Id);
        Assert.Equal(
            $"http://127.0.0.1:{newPort}/NowPlaying.html",
            endpointJson.RootElement.GetProperty("overlayUrl").GetString());
        Assert.Equal(newPort, persistedPort);
        Assert.Equal(newPort, host.Runtime.CurrentPort);
        Assert.Equal(HttpStatusCode.OK, newHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oldDuringGrace.StatusCode);

        await Task.Delay(600);
        using var retiredClient = CreateClient(host.Port);
        await Assert.ThrowsAsync<HttpRequestException>(() => retiredClient.GetAsync("/health"));
    }

    [Fact]
    public async Task FailedPortRebindLeavesTheOldEndpointAndSettingsUntouched()
    {
        await using var host = await TestOverlayHost.StartAsync();
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var occupiedPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var persistCalled = false;

        await Assert.ThrowsAnyAsync<Exception>(() => host.HttpServer.RebindAsync(
            occupiedPort,
            () => persistCalled = true));
        using var oldHealth = await host.Client.GetAsync("/health");

        Assert.False(persistCalled);
        Assert.Equal(host.Port, host.Runtime.CurrentPort);
        Assert.Equal(HttpStatusCode.OK, oldHealth.StatusCode);
    }

    [Fact]
    public async Task PersistenceFailureRollsBackTheCandidateEndpoint()
    {
        await using var host = await TestOverlayHost.StartAsync();
        var candidatePort = ReservePort();

        await Assert.ThrowsAsync<IOException>(() => host.HttpServer.RebindAsync(
            candidatePort,
            () => throw new IOException("settings unavailable")));
        using var oldHealth = await host.Client.GetAsync("/health");
        using var candidateClient = CreateClient(candidatePort);

        Assert.Equal(host.Port, host.Runtime.CurrentPort);
        Assert.Equal(HttpStatusCode.OK, oldHealth.StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => candidateClient.GetAsync("/health"));
    }

    [Fact]
    public async Task RebindAndDisposeCloseEveryTrackedEndpoint()
    {
        var host = await TestOverlayHost.StartAsync();
        var candidatePort = ReservePort();
        var persistedPort = 0;
        using var releasePersistence = new ManualResetEventSlim();
        var persistenceEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var rebind = Task.Run(() => host.HttpServer.RebindAsync(
                candidatePort,
                () =>
                {
                    persistedPort = candidatePort;
                    persistenceEntered.TrySetResult();
                    releasePersistence.Wait();
                }));
            await persistenceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var dispose = host.HttpServer.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            releasePersistence.Set();
            await Task.WhenAll(rebind, dispose);

            Assert.Equal(candidatePort, persistedPort);
            using var oldClient = CreateClient(host.Port);
            using var candidateClient = CreateClient(candidatePort);
            await Assert.ThrowsAsync<HttpRequestException>(() => oldClient.GetAsync("/health"));
            await Assert.ThrowsAsync<HttpRequestException>(() => candidateClient.GetAsync("/health"));
        }
        finally
        {
            releasePersistence.Set();
            await host.DisposeAsync();
        }
    }
}
