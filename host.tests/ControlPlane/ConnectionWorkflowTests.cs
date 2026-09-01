using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.ControlPlane;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.ControlPlane;

public sealed class ConnectionWorkflowTests
{
    [Fact]
    public async Task SpotifyDisconnectFallsBackToTheRememberedWindowsSelection()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var port = ReservePort();
        var settings = new ApplicationSettings
        {
            Port = port,
            Source = SourceSelectionSettings.WindowsMedia("Player.App!Exact"),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App!Exact" },
            Spotify = new SpotifyConnectionSettings { ClientId = "client-id" },
        };
        new SpotifyCredentialStore(paths.SpotifyCredentialsFilePath).Save(
            new SpotifyStoredCredential(
                new SpotifyClientId("client-id"),
                "refresh-token",
                SpotifyAuthorizationRequest.RequiredScope));
        var settingsStore = new ApplicationSettingsStore(
            paths.SettingsFilePath,
            paths.RootDirectory);
        settingsStore.Save(settings);
        var composition = OverlayCompositionRoot.Compose(
            [],
            settings,
            settingsStore,
            paths);
        await using var runtime = composition.Runtime;
        await runtime.StartAsync();

        await composition.Settings.ApplyAsync(
            new SettingsDraft(
                port,
                SourceProvider.SpotifyApi,
                SourceKey.SpotifyApi().InstanceId,
                settings.Appearance,
                settings.Outputs,
                settings.WindowTitle));
        Assert.Equal(SourceProvider.SpotifyApi, settingsStore.Load().Settings.Source.Provider);

        await composition.Spotify.DisconnectAsync();

        var disconnected = settingsStore.Load().Settings;
        Assert.Equal(SourceProvider.WindowsMedia, disconnected.Source.Provider);
        Assert.Equal("Player.App!Exact", disconnected.Source.InstanceId);
        Assert.Null(disconnected.Spotify.ClientId);
        Assert.Equal(
            SourceProvider.WindowsMedia,
            composition.Sources.GetState().ActiveSource!.Key.Provider);
    }

    [Fact]
    public async Task BrowserPlayerCodeRotationPersistsAReplacementAndRevokesTheLease()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var port = ReservePort();
        var settings = new ApplicationSettings
        {
            Port = port,
            Source = SourceSelectionSettings.ExternalPush(),
        };
        var settingsStore = new ApplicationSettingsStore(
            paths.SettingsFilePath,
            paths.RootDirectory);
        settingsStore.Save(settings);
        var composition = OverlayCompositionRoot.Compose(
            [],
            settings,
            settingsStore,
            paths);
        await using var runtime = composition.Runtime;
        await runtime.StartAsync();

        var first = composition.BrowserPlayer.GetConnectionCode();
        var rotated = composition.BrowserPlayer.RotateConnectionCode();

        Assert.StartsWith($"npo1:{port}:", first, StringComparison.Ordinal);
        Assert.StartsWith($"npo1:{port}:", rotated, StringComparison.Ordinal);
        Assert.NotEqual(first, rotated);
        Assert.Equal(SourceStatus.Unavailable, composition.Sources.GetState().Status);
        Assert.True(File.Exists(paths.IngestKeyFilePath));
    }

    [Fact]
    public async Task ProviderDiscoveryKeepsProviderSpecificLifecyclesExplicit()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var settings = new ApplicationSettings { Port = ReservePort() };
        var settingsStore = new ApplicationSettingsStore(paths.SettingsFilePath);
        settingsStore.Save(settings);
        var composition = OverlayCompositionRoot.Compose(
            [],
            settings,
            settingsStore,
            paths);
        await using var runtime = composition.Runtime;

        var spotify = await composition.Sources.RefreshAsync(SourceProvider.SpotifyApi);
        var browser = await composition.Sources.RefreshAsync(SourceProvider.ExternalPush);

        Assert.Equal(SourceKey.SpotifyApi(), Assert.Single(spotify.Sources).Key);
        Assert.Equal(SourceKey.ExternalPush(), Assert.Single(browser.Sources).Key);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
