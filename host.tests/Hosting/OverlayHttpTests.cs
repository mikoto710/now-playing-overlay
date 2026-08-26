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
        using var state = await host.Client.GetAsync("/api/v3/state");
        using var appearance = await host.Client.GetAsync("/api/v3/appearance");
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
        Assert.Equal(3, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("snapshotRevision").GetInt64());
        Assert.Equal("unavailable", json.RootElement.GetProperty("playback").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("track").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("timeline").ValueKind);
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
        using var customAppearance = await host.Client.GetAsync("/api/v3/appearance");
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
    public async Task BrowserProducerAssetSupportsGetAndHeadWithoutCaching()
    {
        await using var host = await TestOverlayHost.StartAsync();

        using var get = await host.Client.GetAsync(BrowserProducerAsset.Path);
        var script = await get.Content.ReadAsStringAsync();
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, BrowserProducerAsset.Path);
        using var head = await host.Client.SendAsync(headRequest);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/javascript", get.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", get.Content.Headers.ContentType.CharSet);
        Assert.Equal("no-store", get.Headers.CacheControl!.ToString());
        Assert.Contains("// ==UserScript==", script, StringComparison.Ordinal);
        Assert.Contains("/ingest/v1/state", script, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

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

        await using (var app = OverlayApplication.Build([], settings, paths))
        {
            Assert.Equal(SourceProvider.ExternalPush, app.GetSourceState().ActiveSource!.Key.Provider);
            Assert.Equal(SourceStatus.Unavailable, app.GetSourceState().Status);
            await app.StartAsync();
            using var client = CreateClient(firstPort);
            var initialToken = app.ExportIngestKey();
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

            rotatedToken = app.RotateIngestKey();
            Assert.NotEqual(initialToken, rotatedToken);
            Assert.Equal(SourceStatus.Unavailable, app.GetSourceState().Status);
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
        await using var restarted = OverlayApplication.Build(
            [],
            settings with { Port = secondPort },
            paths);
        Assert.Equal(rotatedToken, restarted.ExportIngestKey());
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

    [Theory]
    [InlineData("/api/v2/state")]
    [InlineData("/api/v2/events")]
    [InlineData("/api/v2/appearance")]
    [InlineData("/api/v2/artwork/0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task LegacyVersionTwoRoutesAreRemoved(string path)
    {
        await using var host = await TestOverlayHost.StartAsync();

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        host.Source.Publish(SessionObservation.Create(
            SourceDescriptor.SpotifyApi(),
            PlaybackState.Unavailable));
        await host.WaitForRevisionAsync(3);
        using var spotify = await host.Client.GetAsync("/health");
        using var spotifyJson = JsonDocument.Parse(await spotify.Content.ReadAsStringAsync());

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
        Assert.Equal(
            "spotify-api",
            spotifyJson.RootElement.GetProperty("activeSourceProvider").GetString());
        Assert.Equal("unavailable", spotifyJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal(6, spotifyJson.RootElement.EnumerateObject().Count());
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
        using var invalid = await host.Client.GetAsync("/api/v3/artwork/not-a-hash");
        using var missing = await host.Client.GetAsync($"/api/v3/artwork/{new string('0', 64)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
        Assert.Equal(OnePixelPng, bytes);
        Assert.Equal($"/api/v3/artwork/{artworkId}", url);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SseImmediatelySendsFullStateAndThenOnlyNewRevisions()
    {
        await using var host = await TestOverlayHost.StartAsync(heartbeatMilliseconds: 50);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v3/events");
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
        Assert.Contains("\"protocolVersion\":3", initial.Data, StringComparison.Ordinal);
        Assert.Contains("\"snapshotRevision\":0", initial.Data, StringComparison.Ordinal);
        Assert.Contains("\"timeline\":null", initial.Data, StringComparison.Ordinal);
        Assert.Equal("state", changed.Event);
        Assert.EndsWith(":1", changed.Id, StringComparison.Ordinal);
        Assert.Contains("SSE track", changed.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SseConnectionLimitRejectsAdditionalClient()
    {
        await using var host = await TestOverlayHost.StartAsync(maximumSseConnections: 1);
        using var first = await host.Client.GetAsync(
            "/api/v3/events",
            HttpCompletionOption.ResponseHeadersRead);
        using var second = await host.Client.GetAsync(
            "/api/v3/events",
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
            "/api/v3/events",
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
            "/api/v3/events",
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
            var app = OverlayApplication.Build(
                options,
                source,
                OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
                externalIngestHandler: externalIngestHandler);
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
