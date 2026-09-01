using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed partial class OverlayHttpTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task ProductionBrowserPlayerSurvivesKeyRotationAndHostRestart()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var producerId = Guid.Parse("f5b7d897-c655-4cdf-a93b-cd10bd0707d7");
        var firstPort = ReservePort();
        var settings = new ApplicationSettings
        {
            Port = firstPort,
            Source = SourceSelectionSettings.ExternalPush(),
        };
        string rotatedToken;

        var settingsStore = new ApplicationSettingsStore(
            paths.SettingsFilePath,
            paths.RootDirectory);
        var composition = OverlayCompositionRoot.Compose(
            [],
            settings,
            settingsStore,
            paths);
        await using (var app = composition.Runtime)
        {
            Assert.Equal(
                SourceProvider.ExternalPush,
                composition.Sources.GetState().ActiveSource!.Key.Provider);
            Assert.Equal(SourceStatus.Unavailable, composition.Sources.GetState().Status);
            await app.StartAsync();
            using var client = CreateClient(firstPort);
            var initialToken = composition.BrowserPlayer.ExportKey();
            using var stateRequest = CreateIngestRequest(
                ExternalIngestHttpHandler.StatePath,
                initialToken,
                new
                {
                    producerId,
                    producerRevision = 1,
                    playback = "playing",
                    track = new { title = "Before rotation", artist = "Artist" },
                });
            using var stateResponse = await client.SendAsync(stateRequest);
            using var artworkRequest = CreateArtworkRequest(
                initialToken,
                producerId,
                producerRevision: 1,
                OnePixelPng);
            using var artworkResponse = await client.SendAsync(artworkRequest);
            using var published = await WaitForStateAsync(
                client,
                root => root.GetProperty("track").ValueKind == JsonValueKind.Object
                    && root.GetProperty("track").GetProperty("title").GetString()
                        == "Before rotation");
            using var publishedArtwork = await WaitForStateAsync(
                client,
                root => root.GetProperty("artwork").ValueKind == JsonValueKind.Object);
            using var artworkBytes = await client.GetAsync(
                publishedArtwork.RootElement.GetProperty("artwork").GetProperty("url").GetString());
            using var health = await client.GetAsync("/health");
            using var healthJson = JsonDocument.Parse(await health.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NoContent, stateResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, artworkResponse.StatusCode);
            Assert.Equal(
                "external-push",
                published.RootElement.GetProperty("source").GetProperty("provider").GetString());
            Assert.Equal(JsonValueKind.Null, published.RootElement.GetProperty("timeline").ValueKind);
            Assert.Equal(JsonValueKind.Object, publishedArtwork.RootElement.GetProperty("artwork").ValueKind);
            Assert.Equal("image/png", artworkBytes.Content.Headers.ContentType!.MediaType);
            Assert.Equal(OnePixelPng, await artworkBytes.Content.ReadAsByteArrayAsync());
            Assert.Equal(
                "external-push",
                healthJson.RootElement.GetProperty("activeSourceProvider").GetString());
            Assert.Equal("available", healthJson.RootElement.GetProperty("sourceStatus").GetString());

            rotatedToken = composition.BrowserPlayer.RotateConnectionCode().Split(':')[2];
            Assert.NotEqual(initialToken, rotatedToken);
            Assert.Equal(SourceStatus.Unavailable, composition.Sources.GetState().Status);
            using var rejectedRequest = CreateIngestRequest(
                ExternalIngestHttpHandler.StatePath,
                initialToken,
                new
                {
                    producerId,
                    producerRevision = 2,
                    playback = "paused",
                    track = new { title = "Rejected old code" },
                });
            using var rejected = await client.SendAsync(rejectedRequest);
            using var acceptedRequest = CreateIngestRequest(
                ExternalIngestHttpHandler.StatePath,
                rotatedToken,
                new
                {
                    producerId,
                    producerRevision = 2,
                    playback = "paused",
                    track = new { title = "After rotation" },
                });
            using var accepted = await client.SendAsync(acceptedRequest);
            using var rotated = await WaitForStateAsync(
                client,
                root => root.GetProperty("track").ValueKind == JsonValueKind.Object
                    && root.GetProperty("track").GetProperty("title").GetString()
                        == "After rotation");

            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal("paused", rotated.RootElement.GetProperty("playback").GetString());
        }

        var secondPort = ReservePort();
        var restartedComposition = OverlayCompositionRoot.Compose(
            [],
            settings with { Port = secondPort },
            settingsStore,
            paths);
        await using var restarted = restartedComposition.Runtime;
        Assert.Equal(rotatedToken, restartedComposition.BrowserPlayer.ExportKey());
        await restarted.StartAsync();
        using var restartedClient = CreateClient(secondPort);
        using var resumedRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            rotatedToken,
            new
            {
                producerId,
                producerRevision = 3,
                playback = "playing",
                track = new { title = "After restart", artist = "Artist" },
            });
        using var resumed = await restartedClient.SendAsync(resumedRequest);
        using var republished = await WaitForStateAsync(
            restartedClient,
            root => root.GetProperty("track").ValueKind == JsonValueKind.Object
                && root.GetProperty("track").GetProperty("title").GetString()
                    == "After restart");

        Assert.Equal(HttpStatusCode.NoContent, resumed.StatusCode);
        Assert.Equal("After restart", republished.RootElement.GetProperty("track").GetProperty("title").GetString());
    }

    [Fact]
    public async Task AuthenticatedIngestStateAndHeartbeatReachTheSingleProducerLease()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(key, lease));
        var producerId = Guid.Parse("cbb01100-9598-4af9-98f8-d150eed35e91");
        using var stateRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new
            {
                producerId,
                producerRevision = 1,
                playback = "playing",
                track = new
                {
                    title = "Track",
                    artist = "Artist",
                    albumTitle = "Album",
                    trackId = "track-1",
                },
            });
        using var stateResponse = await host.Client.SendAsync(stateRequest);
        using var heartbeatRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.HeartbeatPath,
            token,
            new { producerId });
        using var heartbeatResponse = await host.Client.SendAsync(heartbeatRequest);

        Assert.Equal(HttpStatusCode.NoContent, stateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, heartbeatResponse.StatusCode);
        Assert.Equal("no-store", stateResponse.Headers.CacheControl!.ToString());
        Assert.Equal(producerId, lease.GetCurrentState()!.ProducerId);
        Assert.Equal("Track", lease.GetCurrentState()!.Track!.Title);
    }

    [Fact]
    public async Task AuthenticatedArtworkUploadBindsValidatedBytesToTheCurrentState()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(key, lease));
        var producerId = Guid.Parse("8b2be001-8570-4a53-b418-fccf42162cf7");
        using var stateRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new
            {
                producerId,
                producerRevision = 1,
                playback = "playing",
                track = new { title = "Track", artist = "Artist" },
            });
        using var state = await host.Client.SendAsync(stateRequest);
        using var artworkRequest = CreateArtworkRequest(
            token,
            producerId,
            producerRevision: 1,
            OnePixelPng);

        using var artwork = await host.Client.SendAsync(artworkRequest);

        Assert.Equal(HttpStatusCode.NoContent, state.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, artwork.StatusCode);
        Assert.Equal("no-store", artwork.Headers.CacheControl!.ToString());
        Assert.Equal(OnePixelPng, lease.GetCurrentState()!.Artwork!.Bytes.ToArray());
    }

    [Fact]
    public async Task IngestRequiresExactBearerAuthenticationBeforeReadingState()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(key, lease));
        var payload = new
        {
            producerId = Guid.Parse("3f71d4d4-479c-44e6-b9ae-0a27c6b1e2d7"),
            producerRevision = 1,
            playback = "idle",
        };
        using var missingRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token: null,
            payload);
        using var wrongRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            new string('a', IngestKey.EncodedLength),
            payload);

        using var missing = await host.Client.SendAsync(missingRequest);
        using var wrong = await host.Client.SendAsync(wrongRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Bearer", missing.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public async Task KeyRotationRejectsAnOldRequestThatIsStillUploading()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var originalKey = IngestKey.Generate();
        var originalToken = originalKey.Export();
        var handler = new ExternalIngestHttpHandler(originalKey, lease);
        await using var host = await TestOverlayHost.StartAsync(externalIngestHandler: handler);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            producerId = Guid.Parse("e2352364-659d-4496-9bc1-746b51b21348"),
            producerRevision = 1,
            playback = "idle",
        });
        var content = new BlockingContent(payload, "application/json", "utf-8");
        using var request = new HttpRequestMessage(HttpMethod.Post, ExternalIngestHttpHandler.StatePath)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", originalToken);
        var send = host.Client.SendAsync(request);
        await content.FirstByteWritten.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        var replacement = IngestKey.Generate();
        handler.ReplaceKey(replacement);

        content.Release();
        using var response = await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public async Task KeyRotationRejectsAnOldArtworkRequestThatIsStillUploading()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var originalKey = IngestKey.Generate();
        var originalToken = originalKey.Export();
        var handler = new ExternalIngestHttpHandler(originalKey, lease);
        await using var host = await TestOverlayHost.StartAsync(externalIngestHandler: handler);
        var producerId = Guid.Parse("ae800ab7-c57f-4f7b-b54e-edf5ca077a32");
        lease.ApplyState(ExternalIngestState.Create(
            producerId,
            1,
            PlaybackState.Playing,
            "Track",
            "Artist"));
        var content = new BlockingContent(OnePixelPng, "image/png");
        using var request = CreateArtworkRequest(
            originalToken,
            producerId,
            producerRevision: 1,
            content);
        var send = host.Client.SendAsync(request);
        await content.FirstByteWritten.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        handler.ReplaceKey(IngestKey.Generate());
        content.Release();
        using var response = await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public async Task IngestJsonRejectsUnknownDuplicateAndUnsupportedFields()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(key, lease));
        var producerId = "642c7d48-0b1f-42ac-935f-aa0c256e8084";
        var payloads = new[]
        {
            JsonSerializer.Serialize(new
            {
                producerId,
                producerRevision = 1,
                playback = "idle",
                timeline = new { },
            }),
            JsonSerializer.Serialize(new
            {
                producerId,
                producerRevision = 1,
                playback = "idle",
                artwork = (object?)null,
            }),
            $"{{\"producerId\":\"{producerId}\",\"producerId\":\"{producerId}\",\"producerRevision\":1,\"playback\":\"idle\"}}",
            JsonSerializer.Serialize(new { producerId, producerRevision = 1, playback = 3 }),
        };

        foreach (var payload in payloads)
        {
            using var request = CreateIngestRequest(
                ExternalIngestHttpHandler.StatePath,
                token,
                payload);
            using var response = await host.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public async Task IngestRejectsWrongContentTypeAndOversizedBodies()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(
                key,
                lease,
                new ExternalIngestLimits { MaximumBodyBytes = 128 }));
        var validJson = JsonSerializer.Serialize(new
        {
            producerId = Guid.NewGuid(),
            producerRevision = 1,
            playback = "idle",
        });
        using var wrongType = new HttpRequestMessage(HttpMethod.Post, ExternalIngestHttpHandler.StatePath)
        {
            Content = new StringContent(validJson, Encoding.UTF8, "text/plain"),
        };
        wrongType.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var encoded = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            validJson);
        encoded.Content!.Headers.ContentEncoding.Add("gzip");
        using var wrongCharset = new HttpRequestMessage(HttpMethod.Post, ExternalIngestHttpHandler.StatePath)
        {
            Content = new StringContent(validJson, Encoding.Unicode, "application/json"),
        };
        wrongCharset.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var oversized = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new string('a', 256));

        using var wrongTypeResponse = await host.Client.SendAsync(wrongType);
        using var encodedResponse = await host.Client.SendAsync(encoded);
        using var wrongCharsetResponse = await host.Client.SendAsync(wrongCharset);
        using var oversizedResponse = await host.Client.SendAsync(oversized);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongTypeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, encodedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongCharsetResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        Assert.Null(lease.GetCurrentState());
    }

    [Fact]
    public async Task ArtworkIngestRejectsInvalidTargetsTypesAndBodies()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(
                key,
                lease,
                new ExternalIngestLimits
                {
                    MaximumArtworkBodyBytes = OnePixelPng.Length,
                    MaximumArtworkRequestsPerWindow = 5,
                }));
        var producerId = Guid.Parse("1c39c02a-1f95-4a92-9f72-f2a67aa82cbd");
        lease.ApplyState(ExternalIngestState.Create(
            producerId,
            1,
            PlaybackState.Playing,
            "Track"));
        using var missingHeaders = new HttpRequestMessage(
            HttpMethod.Post,
            ExternalIngestHttpHandler.ArtworkPath)
        {
            Content = new ByteArrayContent(OnePixelPng),
        };
        missingHeaders.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        missingHeaders.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var wrongRevision = CreateArtworkRequest(token, producerId, 2, OnePixelPng);
        using var unsupported = CreateArtworkRequest(token, producerId, 1, OnePixelPng, "image/gif");
        using var mismatched = CreateArtworkRequest(token, producerId, 1, OnePixelPng, "image/jpeg");
        using var oversized = CreateArtworkRequest(
            token,
            producerId,
            1,
            [.. OnePixelPng, 0]);

        using var missingHeadersResponse = await host.Client.SendAsync(missingHeaders);
        using var wrongRevisionResponse = await host.Client.SendAsync(wrongRevision);
        using var unsupportedResponse = await host.Client.SendAsync(unsupported);
        using var mismatchedResponse = await host.Client.SendAsync(mismatched);
        using var oversizedResponse = await host.Client.SendAsync(oversized);

        Assert.Equal(HttpStatusCode.BadRequest, missingHeadersResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, wrongRevisionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupportedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        Assert.Null(lease.GetCurrentState()!.Artwork);
    }

    [Fact]
    public async Task ArtworkRateLimitDoesNotConsumeHeartbeatCapacity()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(
                key,
                lease,
                new ExternalIngestLimits { MaximumArtworkRequestsPerWindow = 1 }));
        var producerId = Guid.Parse("c39c1e64-7097-4c97-8dc2-94ea8a1efebe");
        lease.ApplyState(ExternalIngestState.Create(
            producerId,
            1,
            PlaybackState.Playing,
            "Track"));
        using var acceptedRequest = CreateArtworkRequest(token, producerId, 1, OnePixelPng);
        using var accepted = await host.Client.SendAsync(acceptedRequest);
        using var limitedRequest = CreateArtworkRequest(token, producerId, 1, OnePixelPng);
        using var limited = await host.Client.SendAsync(limitedRequest);
        using var heartbeatRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.HeartbeatPath,
            token,
            new { producerId });
        using var heartbeat = await host.Client.SendAsync(heartbeatRequest);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);
    }

    [Fact]
    public async Task IngestRateLimitAndMethodBoundaryFailClosedWithoutCors()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        var clock = new ManualTimeProvider();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(
                key,
                lease,
                new ExternalIngestLimits { MaximumRequestsPerWindow = 1 },
                clock));
        var producerId = Guid.Parse("663d774b-69a4-42b0-b434-e531bdb0ae64");
        using var stateRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new { producerId, producerRevision = 1, playback = "idle" });
        using var accepted = await host.Client.SendAsync(stateRequest);
        using var limitedRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.HeartbeatPath,
            token,
            new { producerId });
        using var limited = await host.Client.SendAsync(limitedRequest);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var renewedRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.HeartbeatPath,
            token,
            new { producerId });
        using var renewed = await host.Client.SendAsync(renewedRequest);
        using var options = new HttpRequestMessage(HttpMethod.Options, ExternalIngestHttpHandler.StatePath);
        options.Headers.Add("Origin", "https://example.invalid");
        using var method = await host.Client.SendAsync(options);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.Equal("1", limited.Headers.RetryAfter!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, renewed.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, method.StatusCode);
        Assert.Equal("POST", method.Content.Headers.Allow.Single());
        Assert.False(method.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task IngestConflictsAndHeartbeatWithoutLeaseReturnConflict()
    {
        var lease = new ExternalProducerLease(TimeSpan.FromMinutes(1));
        var key = IngestKey.Generate();
        var token = key.Export();
        await using var host = await TestOverlayHost.StartAsync(
            externalIngestHandler: new ExternalIngestHttpHandler(key, lease));
        var firstProducer = Guid.Parse("50834c77-2056-432f-b255-e191fd668b6d");
        var secondProducer = Guid.Parse("66f72aeb-f139-4944-90f1-9811f33d21f4");
        using var missingHeartbeatRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.HeartbeatPath,
            token,
            new { producerId = firstProducer });
        using var missingHeartbeat = await host.Client.SendAsync(missingHeartbeatRequest);
        using var claimRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new { producerId = firstProducer, producerRevision = 2, playback = "idle" });
        using var claim = await host.Client.SendAsync(claimRequest);
        using var staleRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new { producerId = firstProducer, producerRevision = 2, playback = "idle" });
        using var stale = await host.Client.SendAsync(staleRequest);
        using var foreignRequest = CreateIngestRequest(
            ExternalIngestHttpHandler.StatePath,
            token,
            new { producerId = secondProducer, producerRevision = 1, playback = "idle" });
        using var foreign = await host.Client.SendAsync(foreignRequest);

        Assert.Equal(HttpStatusCode.Conflict, missingHeartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, claim.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        Assert.Equal(firstProducer, lease.GetCurrentState()!.ProducerId);
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
            OverlayRuntimeGraph graph,
            FakeSessionSource source,
            HttpClient client,
            int port)
        {
            Runtime = graph.Runtime;
            HttpServer = graph.HttpServer;
            Appearance = graph.Appearance;
            Source = source;
            Client = client;
            Port = port;
        }

        public AppearanceState Appearance { get; }

        public OverlayHttpServer HttpServer { get; }

        public OverlayRuntime Runtime { get; }

        public HttpClient Client { get; }

        public int Port { get; }

        public FakeSessionSource Source { get; }

        public static async Task<TestOverlayHost> StartAsync(
            int heartbeatMilliseconds = 100,
            int maximumSseConnections = 4,
            int maximumConcurrentConnections = 32,
            int rebindGraceMilliseconds = 100,
            ExternalIngestHttpHandler? externalIngestHandler = null)
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
            var graph = OverlayCompositionRoot.BuildRuntime(
                options,
                source,
                OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
                externalIngestHandler: externalIngestHandler);
            await graph.Runtime.StartAsync();
            var client = CreateClient(port);
            return new TestOverlayHost(graph, source, client, port);
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
            await Runtime.StopAsync();
            await Runtime.DisposeAsync();
        }

        private async Task<JsonDocument> WaitForStateAsync(Func<JsonElement, bool> predicate)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var response = await Client.GetAsync("/api/v3/state");
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

    private static HttpRequestMessage CreateIngestRequest(
        string path,
        string? token,
        object payload)
    {
        return CreateIngestRequest(path, token, JsonSerializer.Serialize(payload));
    }

    private static HttpRequestMessage CreateIngestRequest(
        string path,
        string? token,
        string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static HttpRequestMessage CreateArtworkRequest(
        string token,
        Guid producerId,
        long producerRevision,
        byte[] body,
        string contentType = "image/png")
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return CreateArtworkRequest(token, producerId, producerRevision, content);
    }

    private static HttpRequestMessage CreateArtworkRequest(
        string token,
        Guid producerId,
        long producerRevision,
        HttpContent content)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            ExternalIngestHttpHandler.ArtworkPath)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(
            ExternalIngestHttpHandler.ProducerIdHeader,
            producerId.ToString("D"));
        request.Headers.Add(
            ExternalIngestHttpHandler.ProducerRevisionHeader,
            producerRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }

    private static async Task<JsonDocument> WaitForStateAsync(
        HttpClient client,
        Func<JsonElement, bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var response = await client.GetAsync("/api/v3/state");
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (predicate(document.RootElement))
            {
                return document;
            }

            document.Dispose();
            await Task.Delay(10);
        }

        throw new TimeoutException("Production state did not reach the expected value.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return _timestamp;
        }

        public void Advance(TimeSpan duration)
        {
            _timestamp = checked(_timestamp + duration.Ticks);
        }
    }

    private sealed class BlockingContent : HttpContent
    {
        private readonly byte[] _body;
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstByteWritten =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingContent(byte[] body, string contentType, string? charset = null)
        {
            _body = body;
            Headers.ContentType = new MediaTypeHeaderValue(contentType)
            {
                CharSet = charset,
            };
        }

        public Task FirstByteWritten => _firstByteWritten.Task;

        public void Release()
        {
            _release.TrySetResult();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await stream.WriteAsync(_body.AsMemory(0, 1));
            await stream.FlushAsync();
            _firstByteWritten.TrySetResult();
            await _release.Task;
            await stream.WriteAsync(_body.AsMemory(1));
        }
    }
}
