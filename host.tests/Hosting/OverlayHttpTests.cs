using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class OverlayHttpTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

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

        Assert.Equal(host.Port, host.App.CurrentPort);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SpotifyCallbackUsesTheHostPortOnlyDuringPendingAuthorization()
    {
        var port = ReservePort();
        var source = new FakeSessionSource();
        var callbackBroker = new SpotifyAuthorizationCallbackBroker();
        await using var app = OverlayApplication.Build(
            new HostOptions { Port = port },
            source,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            callbackBroker);
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

    [Fact]
    public async Task ProductionPageStateAndAppearanceUseNoStoreAndExactContracts()
    {
        await using var host = await TestOverlayHost.StartAsync();

        using var page = await host.Client.GetAsync("/NowPlaying.html");
        using var state = await host.Client.GetAsync("/api/v2/state");
        using var appearance = await host.Client.GetAsync("/api/v2/appearance");
        var html = await page.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
        using var appearanceJson = JsonDocument.Parse(await appearance.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("id=\"now-playing\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol diagnostic", html, StringComparison.Ordinal);
        Assert.Equal("no-store", page.Headers.CacheControl!.ToString());
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        Assert.Contains(
            "frame-ancestors 'none'",
            page.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", state.Headers.CacheControl!.ToString());
        Assert.Equal("no-store", appearance.Headers.CacheControl!.ToString());
        Assert.Equal(2, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("snapshotRevision").GetInt64());
        Assert.Equal("unavailable", json.RootElement.GetProperty("playback").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("track").ValueKind);
        Assert.Equal(17, appearanceJson.RootElement.EnumerateObject().Count());
        Assert.Equal(3, appearanceJson.RootElement.GetProperty("appearanceVersion").GetInt32());
        Assert.Equal("default", appearanceJson.RootElement.GetProperty("preset").GetString());
        Assert.Equal("#25C7A0", appearanceJson.RootElement.GetProperty("artistColor").GetString());
        Assert.Equal("#FFFFFF", appearanceJson.RootElement.GetProperty("trackColor").GetString());
        Assert.Equal("#1B1D20", appearanceJson.RootElement.GetProperty("backgroundColor").GetString());
        Assert.Equal(
            100,
            appearanceJson.RootElement.GetProperty("backgroundOpacityPercent").GetInt32());
        Assert.Equal(0, appearanceJson.RootElement.GetProperty("cornerRadius").GetInt32());
        Assert.Equal(JsonValueKind.Null, appearanceJson.RootElement.GetProperty("fontFamily").ValueKind);
        Assert.Equal(16, appearanceJson.RootElement.GetProperty("artistFontSize").GetInt32());
        Assert.Equal(600, appearanceJson.RootElement.GetProperty("artistFontWeight").GetInt32());
        Assert.Equal(22, appearanceJson.RootElement.GetProperty("trackFontSize").GetInt32());
        Assert.Equal(700, appearanceJson.RootElement.GetProperty("trackFontWeight").GetInt32());
        Assert.True(appearanceJson.RootElement.GetProperty("artworkVisible").GetBoolean());
        Assert.Equal(70, appearanceJson.RootElement.GetProperty("artworkSize").GetInt32());
        Assert.Equal("left", appearanceJson.RootElement.GetProperty("artworkPosition").GetString());
        Assert.Equal("contain", appearanceJson.RootElement.GetProperty("artworkFit").GetString());
        Assert.Equal(0, appearanceJson.RootElement.GetProperty("artworkCornerRadius").GetInt32());

        host.App.SetAppearance(new AppearanceSettings
        {
            Preset = AppearancePreset.Custom,
            Custom = new CustomAppearanceSettings
            {
                ArtistColor = "#123456",
                TrackColor = "#ABCDEF",
                BackgroundColor = "#102030",
                BackgroundOpacityPercent = 65,
                CornerRadius = 12,
                FontFamily = "Segoe UI",
                ArtistFontSize = 18,
                ArtistFontWeight = 500,
                TrackFontSize = 24,
                TrackFontWeight = 600,
                ArtworkVisible = true,
                ArtworkSize = 48,
                ArtworkPosition = ArtworkPosition.Right,
                ArtworkFit = ArtworkFit.Cover,
                ArtworkCornerRadius = 8,
            },
        });
        using var customAppearance = await host.Client.GetAsync("/api/v2/appearance");
        using var customJson = JsonDocument.Parse(await customAppearance.Content.ReadAsStringAsync());
        Assert.Equal("custom", customJson.RootElement.GetProperty("preset").GetString());
        Assert.Equal("#123456", customJson.RootElement.GetProperty("artistColor").GetString());
        Assert.Equal(
            65,
            customJson.RootElement.GetProperty("backgroundOpacityPercent").GetInt32());
        Assert.Equal(12, customJson.RootElement.GetProperty("cornerRadius").GetInt32());
        Assert.Equal("Segoe UI", customJson.RootElement.GetProperty("fontFamily").GetString());
        Assert.Equal(18, customJson.RootElement.GetProperty("artistFontSize").GetInt32());
        Assert.Equal(500, customJson.RootElement.GetProperty("artistFontWeight").GetInt32());
        Assert.Equal(24, customJson.RootElement.GetProperty("trackFontSize").GetInt32());
        Assert.Equal(600, customJson.RootElement.GetProperty("trackFontWeight").GetInt32());
        Assert.True(customJson.RootElement.GetProperty("artworkVisible").GetBoolean());
        Assert.Equal(48, customJson.RootElement.GetProperty("artworkSize").GetInt32());
        Assert.Equal("right", customJson.RootElement.GetProperty("artworkPosition").GetString());
        Assert.Equal("cover", customJson.RootElement.GetProperty("artworkFit").GetString());
        Assert.Equal(8, customJson.RootElement.GetProperty("artworkCornerRadius").GetInt32());
    }

    [Fact]
    public async Task ResponsesDoNotIdentifyTheServerAndUnknownRoutesFailClosed()
    {
        await using var host = await TestOverlayHost.StartAsync();

        using var health = await host.Client.GetAsync("/health");
        using var missing = await host.Client.GetAsync("/not-an-overlay-route");

        Assert.False(health.Headers.Contains("Server"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("nosniff", missing.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task HealthReportsReadyAvailabilityBindingAndFaultsWithoutMetadata()
    {
        await using var host = await TestOverlayHost.StartAsync();
        using var initial = await host.Client.GetAsync("/health");
        using var initialJson = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        host.Source.Publish(Playing("Bound track"));
        await host.WaitForRevisionAsync(1);
        using var bound = await host.Client.GetAsync("/health");
        using var boundJson = JsonDocument.Parse(await bound.Content.ReadAsStringAsync());
        host.Source.Publish(SessionObservation.Create(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Unavailable));
        await host.WaitForRevisionAsync(2);
        using var unavailable = await host.Client.GetAsync("/health");
        using var unavailableJson = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());

        host.Source.PublishError(new InvalidOperationException("fake source failure"));
        using var faulted = await host.WaitForHealthAsync(HttpStatusCode.ServiceUnavailable);
        var faultedText = await faulted.Content.ReadAsStringAsync();

        Assert.Equal("ready", initialJson.RootElement.GetProperty("hostStatus").GetString());
        Assert.Equal(JsonValueKind.Null, initialJson.RootElement.GetProperty("activeSourceProvider").ValueKind);
        Assert.Equal("unconfigured", initialJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal(
            "windows-media",
            boundJson.RootElement.GetProperty("activeSourceProvider").GetString());
        Assert.Equal("available", boundJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal(
            "windows-media",
            unavailableJson.RootElement.GetProperty("activeSourceProvider").GetString());
        Assert.Equal("unavailable", unavailableJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Contains("\"hostStatus\":\"faulted\"", faultedText, StringComparison.Ordinal);
        Assert.DoesNotContain("fake source failure", faultedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Bound track", faultedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtworkEndpointIsContentAddressedImmutableAndRejectsUnknownIds()
    {
        await using var host = await TestOverlayHost.StartAsync();
        host.Source.Publish(Playing(
            "Artwork track",
            new ImmediateArtworkReader(ArtworkPayload.Create(OnePixelPng))));
        var state = await host.WaitForArtworkAsync();
        var artwork = state.RootElement.GetProperty("artwork");
        var artworkId = artwork.GetProperty("artworkId").GetString()!;
        var url = artwork.GetProperty("url").GetString()!;

        using var response = await host.Client.GetAsync(url);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var invalid = await host.Client.GetAsync("/api/v2/artwork/not-a-hash");
        using var missing = await host.Client.GetAsync($"/api/v2/artwork/{new string('0', 64)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
        Assert.Equal(OnePixelPng, bytes);
        Assert.Equal($"/api/v2/artwork/{artworkId}", url);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SseImmediatelySendsFullStateAndThenOnlyNewRevisions()
    {
        await using var host = await TestOverlayHost.StartAsync(heartbeatMilliseconds: 50);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "old-instance:999");
        using var response = await host.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var initial = await ReadSseEventAsync(reader);
        host.Source.Publish(Playing("SSE track"));
        var changed = await ReadSseEventAsync(reader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("state", initial.Event);
        Assert.EndsWith(":0", initial.Id, StringComparison.Ordinal);
        Assert.Contains("\"snapshotRevision\":0", initial.Data, StringComparison.Ordinal);
        Assert.Equal("state", changed.Event);
        Assert.EndsWith(":1", changed.Id, StringComparison.Ordinal);
        Assert.Contains("SSE track", changed.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SseConnectionLimitRejectsAdditionalClient()
    {
        await using var host = await TestOverlayHost.StartAsync(maximumSseConnections: 1);
        using var first = await host.Client.GetAsync(
            "/api/v2/events",
            HttpCompletionOption.ResponseHeadersRead);
        using var second = await host.Client.GetAsync(
            "/api/v2/events",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("1", second.Headers.RetryAfter!.ToString());
    }

    [Fact]
    public async Task TotalActiveRequestLimitIsSharedWithSseConnections()
    {
        await using var host = await TestOverlayHost.StartAsync(
            maximumSseConnections: 1,
            maximumConcurrentConnections: 1);
        using var stream = await host.Client.GetAsync(
            "/api/v2/events",
            HttpCompletionOption.ResponseHeadersRead);

        using var rejected = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Equal("1", rejected.Headers.RetryAfter!.ToString());
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
        using var stateHead = await SendHeadAsync(host.Client, "/api/v2/state");
        using var artworkHead = await SendHeadAsync(host.Client, artworkUrl);
        using var healthHead = await SendHeadAsync(host.Client, "/health");
        using var post = await host.Client.PostAsync("/api/v2/state", content: null);

        AssertHead(pageHead, "text/html", "no-store");
        AssertHead(stateHead, "application/json", "no-store");
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
        var app = OverlayApplication.Build(
            new HostOptions { Port = port },
            source,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly));
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
        var app = OverlayApplication.Build(
            new HostOptions { Port = port },
            new FakeSessionSource(),
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly));

        await Assert.ThrowsAnyAsync<Exception>(async () => await app.StartAsync());

        await app.DisposeAsync();
    }

    [Fact]
    public async Task LivePortRebindPublishesTheNewEndpointAndRetiresTheOldPort()
    {
        await using var host = await TestOverlayHost.StartAsync(rebindGraceMilliseconds: 300);
        using var events = await host.Client.GetAsync(
            "/api/v2/events",
            HttpCompletionOption.ResponseHeadersRead);
        await using var eventStream = await events.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(eventStream);
        _ = await ReadSseEventAsync(reader);
        var newPort = ReservePort();
        var persistedPort = 0;

        await host.App.RebindPortAsync(newPort, () => persistedPort = newPort);
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
        Assert.Equal(newPort, host.App.CurrentPort);
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

        await Assert.ThrowsAnyAsync<Exception>(() => host.App.RebindPortAsync(
            occupiedPort,
            () => persistCalled = true));
        using var oldHealth = await host.Client.GetAsync("/health");

        Assert.False(persistCalled);
        Assert.Equal(host.Port, host.App.CurrentPort);
        Assert.Equal(HttpStatusCode.OK, oldHealth.StatusCode);
    }

    [Fact]
    public async Task PersistenceFailureRollsBackTheCandidateEndpoint()
    {
        await using var host = await TestOverlayHost.StartAsync();
        var candidatePort = ReservePort();

        await Assert.ThrowsAsync<IOException>(() => host.App.RebindPortAsync(
            candidatePort,
            () => throw new IOException("settings unavailable")));
        using var oldHealth = await host.Client.GetAsync("/health");
        using var candidateClient = CreateClient(candidatePort);

        Assert.Equal(host.Port, host.App.CurrentPort);
        Assert.Equal(HttpStatusCode.OK, oldHealth.StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => candidateClient.GetAsync("/health"));
    }

    private static SessionObservation Playing(string title, IArtworkReader? artworkReader = null)
    {
        return SessionObservation.Create(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create(title, "Artist", "Album"),
            artworkReader);
    }

    private static async Task<SseEvent> ReadSseEventAsync(StreamReader reader)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            string? eventName = null;
            string? id = null;
            string? data = null;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellation.Token)) is not null && line.Length > 0)
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventName = line[7..];
                }
                else if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    id = line[4..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line[6..];
                }
            }

            if (eventName is not null && data is not null)
            {
                return new SseEvent(eventName, id, data);
            }
        }
    }

    private static Task<HttpResponseMessage> SendHeadAsync(HttpClient client, string path)
    {
        return client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
    }

    private static void AssertHead(
        HttpResponseMessage response,
        string mediaType,
        string cacheControl)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
        Assert.True(response.Content.Headers.ContentLength > 0);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(cacheControl, response.Headers.CacheControl!.ToString());
    }

    private static async Task<int> SendRawRequestAsync(int port, string additionalHeaders)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var request = System.Text.Encoding.ASCII.GetBytes(
            $"GET /health HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n{additionalHeaders}Connection: close\r\n\r\n");
        await stream.WriteAsync(request);
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
        var statusLine = await reader.ReadLineAsync();
        Assert.NotNull(statusLine);
        return int.Parse(statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
    }

    private sealed record SseEvent(string Event, string? Id, string Data);

    private sealed class ImmediateArtworkReader(ArtworkPayload payload) : IArtworkReader
    {
        public ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ArtworkPayload?>(payload);
        }
    }

    private sealed class TestOverlayHost : IAsyncDisposable
    {
        private TestOverlayHost(
            OverlayApplication app,
            FakeSessionSource source,
            HttpClient client,
            int port)
        {
            App = app;
            Source = source;
            Client = client;
            Port = port;
        }

        public OverlayApplication App { get; }

        public HttpClient Client { get; }

        public int Port { get; }

        public FakeSessionSource Source { get; }

        public static async Task<TestOverlayHost> StartAsync(
            int heartbeatMilliseconds = 100,
            int maximumSseConnections = 4,
            int maximumConcurrentConnections = 32,
            int rebindGraceMilliseconds = 100)
        {
            var port = ReservePort();
            var source = new FakeSessionSource();
            var options = new HostOptions
            {
                Port = port,
                SseHeartbeatInterval = TimeSpan.FromMilliseconds(heartbeatMilliseconds),
                MaximumSseConnections = maximumSseConnections,
                MaximumConcurrentConnections = maximumConcurrentConnections,
                PortRebindGracePeriod = TimeSpan.FromMilliseconds(rebindGraceMilliseconds),
            };
            var app = OverlayApplication.Build(
                options,
                source,
                OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly));
            await app.StartAsync();
            var client = CreateClient(port);
            return new TestOverlayHost(app, source, client, port);
        }

        public async Task WaitForRevisionAsync(long revision)
        {
            using var document = await WaitForStateAsync(root =>
                root.GetProperty("snapshotRevision").GetInt64() >= revision);
        }

        public async Task<JsonDocument> WaitForArtworkAsync()
        {
            return await WaitForStateAsync(root =>
                root.GetProperty("artwork").ValueKind == JsonValueKind.Object);
        }

        public async Task<HttpResponseMessage> WaitForHealthAsync(HttpStatusCode statusCode)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var response = await Client.GetAsync("/health");
                if (response.StatusCode == statusCode)
                {
                    return response;
                }

                response.Dispose();
                await Task.Delay(10);
            }

            throw new TimeoutException($"Health did not reach {(int)statusCode}.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }

        private async Task<JsonDocument> WaitForStateAsync(Func<JsonElement, bool> predicate)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var response = await Client.GetAsync("/api/v2/state");
                var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (predicate(document.RootElement))
                {
                    return document;
                }

                document.Dispose();
                await Task.Delay(10);
            }

            throw new TimeoutException("State did not reach the expected value.");
        }

    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpClient CreateClient(int port)
    {
        return new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }
}
