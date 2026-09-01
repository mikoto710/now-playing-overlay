using System.Net;
using System.Text.Json;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed partial class OverlayHttpTests
{
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
        Assert.Equal(100, appearanceJson.RootElement.GetProperty("backgroundOpacityPercent").GetInt32());
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

        host.Appearance.Set(new AppearanceSettings
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
        Assert.Equal(65, customJson.RootElement.GetProperty("backgroundOpacityPercent").GetInt32());
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
        host.Source.Publish(SessionObservation.Create(SourceDescriptor.WindowsMedia("Player.App"), PlaybackState.Unavailable));
        await host.WaitForRevisionAsync(2);
        using var unavailable = await host.Client.GetAsync("/health");
        using var unavailableJson = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        host.Source.Publish(SessionObservation.Create(SourceDescriptor.SpotifyApi(), PlaybackState.Unavailable));
        await host.WaitForRevisionAsync(3);
        using var spotify = await host.Client.GetAsync("/health");
        using var spotifyJson = JsonDocument.Parse(await spotify.Content.ReadAsStringAsync());

        host.Source.PublishError(new InvalidOperationException("fake source failure"));
        using var faulted = await host.WaitForHealthAsync(HttpStatusCode.ServiceUnavailable);
        var faultedText = await faulted.Content.ReadAsStringAsync();

        Assert.Equal("ready", initialJson.RootElement.GetProperty("hostStatus").GetString());
        Assert.Equal(JsonValueKind.Null, initialJson.RootElement.GetProperty("activeSourceProvider").ValueKind);
        Assert.Equal("unconfigured", initialJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal("windows-media", boundJson.RootElement.GetProperty("activeSourceProvider").GetString());
        Assert.Equal("available", boundJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal("windows-media", unavailableJson.RootElement.GetProperty("activeSourceProvider").GetString());
        Assert.Equal("unavailable", unavailableJson.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal("spotify-api", spotifyJson.RootElement.GetProperty("activeSourceProvider").GetString());
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
        host.Source.Publish(Playing("Artwork track", new ImmediateArtworkReader(ArtworkPayload.Create(OnePixelPng))));
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
}
