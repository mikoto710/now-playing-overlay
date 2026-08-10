using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class OverlayHttpTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task ServerBindsOnlyIpv4LoopbackAndFiltersHost()
    {
        await using var host = await TestOverlayHost.StartAsync();
        var addresses = host.App.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "localhost";

        using var response = await host.Client.SendAsync(request);

        Assert.Single(addresses);
        Assert.Equal($"http://127.0.0.1:{host.Port}", addresses.Single());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DiagnosticPageAndStateUseNoStoreAndSecurityHeaders()
    {
        await using var host = await TestOverlayHost.StartAsync();

        using var page = await host.Client.GetAsync("/NowPlaying.html");
        using var state = await host.Client.GetAsync("/api/v1/state");
        var html = await page.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(await state.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("protocol diagnostic", html, StringComparison.Ordinal);
        Assert.Equal("no-store", page.Headers.CacheControl!.ToString());
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", state.Headers.CacheControl!.ToString());
        Assert.Equal(1, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("snapshotRevision").GetInt64());
        Assert.Equal("unavailable", json.RootElement.GetProperty("playback").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("track").ValueKind);
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

        host.Source.PublishError(new InvalidOperationException("fake source failure"));
        using var faulted = await host.WaitForHealthAsync(HttpStatusCode.ServiceUnavailable);
        var faultedText = await faulted.Content.ReadAsStringAsync();

        Assert.Equal("ready", initialJson.RootElement.GetProperty("hostStatus").GetString());
        Assert.True(initialJson.RootElement.GetProperty("sessionManagerAvailable").GetBoolean());
        Assert.False(initialJson.RootElement.GetProperty("spotifySessionBound").GetBoolean());
        Assert.True(boundJson.RootElement.GetProperty("spotifySessionBound").GetBoolean());
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
            new ImmediateArtworkReader(ArtworkPayload.Create(OnePixelPng, "application/octet-stream"))));
        var state = await host.WaitForArtworkAsync();
        var artwork = state.RootElement.GetProperty("artwork");
        var artworkId = artwork.GetProperty("artworkId").GetString()!;
        var url = artwork.GetProperty("url").GetString()!;

        using var response = await host.Client.GetAsync(url);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var invalid = await host.Client.GetAsync("/api/v1/artwork/not-a-hash");
        using var missing = await host.Client.GetAsync($"/api/v1/artwork/{new string('0', 64)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
        Assert.Equal(OnePixelPng, bytes);
        Assert.Equal($"/api/v1/artwork/{artworkId}", url);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SseImmediatelySendsFullStateAndThenOnlyNewRevisions()
    {
        await using var host = await TestOverlayHost.StartAsync(heartbeatMilliseconds: 50);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events");
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
            "/api/v1/events",
            HttpCompletionOption.ResponseHeadersRead);
        using var second = await host.Client.GetAsync(
            "/api/v1/events",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("1", second.Headers.RetryAfter!.Delta?.TotalSeconds.ToString() ?? second.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task HeadReturnsHeadersWithoutBodyAndUnsupportedMethodIsRejected()
    {
        await using var host = await TestOverlayHost.StartAsync();
        using var head = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/v1/state"));
        using var post = await host.Client.PostAsync("/api/v1/state", content: null);

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.True(head.Content.Headers.ContentLength > 0);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    private static SessionObservation Playing(string title, IArtworkReader? artworkReader = null)
    {
        return SessionObservation.Create(
            "Spotify.exe",
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

            if (eventName is not null && id is not null && data is not null)
            {
                return new SseEvent(eventName, id, data);
            }
        }
    }

    private sealed record SseEvent(string Event, string Id, string Data);

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
        private TestOverlayHost(WebApplication app, HttpClient client, int port)
        {
            App = app;
            Client = client;
            Port = port;
            Source = app.Services.GetRequiredService<FakeSessionSource>();
        }

        public WebApplication App { get; }

        public HttpClient Client { get; }

        public int Port { get; }

        public FakeSessionSource Source { get; }

        public static async Task<TestOverlayHost> StartAsync(
            int heartbeatMilliseconds = 100,
            int maximumSseConnections = 4)
        {
            var port = ReservePort();
            var app = OverlayApplication.Build(
            [
                $"--Host:Port={port}",
                "--Host:RunFakeScenario=false",
                $"--Host:SseHeartbeatInterval=00:00:00.{heartbeatMilliseconds:D3}",
                $"--Host:MaximumSseConnections={maximumSseConnections}",
                "--Logging:LogLevel:Default=Warning",
            ]);
            await app.StartAsync();
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };
            return new TestOverlayHost(app, client, port);
        }

        public async Task<JsonDocument> WaitForRevisionAsync(long revision)
        {
            return await WaitForStateAsync(root =>
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
                using var response = await Client.GetAsync("/api/v1/state");
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

        private static int ReservePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
