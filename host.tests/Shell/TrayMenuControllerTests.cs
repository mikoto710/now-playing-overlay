using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.ControlPlane;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.Shell;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class TrayMenuControllerTests
{
    [Fact]
    public void ExposesSupportedOverlayPreviewSizesAndBuildsTheirUrls()
    {
        Assert.Collection(
            TrayMenuController.OverlayPreviewOptions,
            option => Assert.Equal(new OverlayPreviewOption(1, 350, 70), option),
            option => Assert.Equal(new OverlayPreviewOption(2, 700, 140), option),
            option => Assert.Equal(new OverlayPreviewOption(3, 1050, 210), option),
            option => Assert.Equal(new OverlayPreviewOption(4, 1400, 280), option),
            option => Assert.Equal(new OverlayPreviewOption(5, 1750, 350), option));

        Assert.Equal(
            "http://127.0.0.1:13000/NowPlaying.html?previewScale=3",
            OverlayEndpoint.BuildPreviewUrl(13000, previewScale: 3));
    }

    [Fact]
    public async Task UsesLiveEffectivePortAndPersistsOnlyAChangedPort()
    {
        var initialPort = ReservePort();
        await using var host = await ControllerTestHost.StartAsync(
            new ApplicationSettings { Port = initialPort },
            persistInitialSettings: false);
        var changedPort = ReservePort();

        var unchanged = await host.Controller.SavePortAsync(initialPort);
        var fileExistsAfterUnchanged = File.Exists(host.Paths.SettingsFilePath);
        var changed = await host.Controller.SavePortAsync(changedPort);

        Assert.False(unchanged.Changed);
        Assert.False(fileExistsAfterUnchanged);
        Assert.True(changed.Changed);
        Assert.Equal(changedPort, host.Controller.EffectivePort);
        Assert.Equal(changedPort, host.SettingsStore.Load().Settings.Port);
        Assert.Equal(changed.OverlayUrl, host.Controller.OverlayUrl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task RejectsInvalidPort(int port)
    {
        await using var host = await ControllerTestHost.StartAsync(
            new ApplicationSettings { Port = ReservePort() });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            host.Controller.SavePortAsync(port));
    }

    [Fact]
    public async Task DoesNotPersistWhenRebindingFails()
    {
        var initialPort = ReservePort();
        await using var host = await ControllerTestHost.StartAsync(
            new ApplicationSettings { Port = initialPort });
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var unavailablePort = ((IPEndPoint)occupied.LocalEndpoint).Port;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.Controller.SavePortAsync(unavailablePort));

        Assert.Equal(initialPort, host.Controller.EffectivePort);
        Assert.Equal(initialPort, host.SettingsStore.Load().Settings.Port);
    }

    [Fact]
    public async Task RefreshSourcesForwardsTheRequestedProvider()
    {
        await using var host = await ControllerTestHost.StartAsync(
            new ApplicationSettings { Port = ReservePort() });

        var actual = await host.Controller.RefreshSourcesAsync(SourceProvider.SpotifyApi);

        Assert.Equal(SourceKey.SpotifyApi(), Assert.Single(actual.Sources).Key);
    }

    [Fact]
    public async Task SavingPortPreservesTheExactSourceSelection()
    {
        var initialPort = ReservePort();
        var settings = new ApplicationSettings
        {
            Port = initialPort,
            Source = SourceSelectionSettings.WindowsMedia("Player.App"),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App" },
        };
        await using var host = await ControllerTestHost.StartAsync(settings);
        var changedPort = ReservePort();

        await host.Controller.SavePortAsync(changedPort);

        var saved = host.SettingsStore.Load().Settings;
        Assert.Equal(changedPort, saved.Port);
        Assert.Equal("Player.App", saved.Source.InstanceId);
        Assert.Equal("Player.App", saved.WindowsMedia.LastInstanceId);
    }

    [Fact]
    public async Task SavingWindowTitleParserReconfiguresTheActiveSourceWithoutReselectingIt()
    {
        var target = new WindowTitleTargetSettings
        {
            ProcessName = "Player",
            ExecutablePath = @"C:\Apps\Player.exe",
            WindowClass = "PlayerWindow",
        };
        var original = new WindowTitleSettings { Target = target };
        var settings = new ApplicationSettings
        {
            Port = ReservePort(),
            Source = SourceSelectionSettings.WindowTitle(target.InstanceId),
            WindowTitle = original,
        };
        await using var host = await ControllerTestHost.StartAsync(settings);
        var sourceKey = host.Composition.Sources.GetState().ActiveSource!.Key;
        var updated = original with
        {
            ParseMode = WindowTitleParseMode.Split,
            Separator = " | ",
            LeftField = WindowTitleField.Title,
        };

        await host.Controller.ApplySettingsAsync(new SettingsDraft(
            settings.Port,
            SourceProvider.WindowTitle,
            target.InstanceId,
            settings.Appearance,
            settings.Outputs,
            updated));

        Assert.Equal(updated, host.SettingsStore.Load().Settings.WindowTitle);
        Assert.Equal(sourceKey, host.Composition.Sources.GetState().ActiveSource!.Key);
    }

    [Fact]
    public async Task CombinedSettingsSavePersistsAndActivatesCustomAppearance()
    {
        var settings = new ApplicationSettings { Port = ReservePort() };
        await using var host = await ControllerTestHost.StartAsync(settings);
        var appearance = new AppearanceSettings
        {
            Preset = AppearancePreset.Custom,
            Custom = new CustomAppearanceSettings
            {
                ArtistColor = "#123456",
                TrackColor = "#ABCDEF",
                BackgroundColor = "#102030",
                BackgroundOpacityPercent = 65,
                CornerRadius = 12,
            },
        };
        var outputs = new OutputSettings
        {
            Json = new JsonOutputSettings
            {
                Enabled = true,
                FilePath = Path.Combine(host.Directory.Path, "state.json"),
            },
        };
        var expectedOutputs = host.SettingsStore.Load().Settings.Outputs with
        {
            Json = outputs.Json,
        };

        var result = await host.Controller.ApplySettingsAsync(new SettingsDraft(
            settings.Port,
            SourceProvider.WindowsMedia,
            "Player.App!Exact",
            appearance,
            expectedOutputs,
            settings.WindowTitle));

        var saved = host.SettingsStore.Load().Settings;
        Assert.False(result.PortChanged);
        Assert.Equal("Player.App!Exact", saved.Source.InstanceId);
        Assert.Equal("Player.App!Exact", saved.WindowsMedia.LastInstanceId);
        Assert.Equal(appearance, saved.Appearance);
        Assert.Equal(expectedOutputs, saved.Outputs);
        Assert.Equal(
            SourceProvider.WindowsMedia,
            host.Composition.Sources.GetState().ActiveSource!.Key.Provider);
    }

    [Fact]
    public async Task SpotifyConnectionAndProviderSelectionHaveSeparateLifecycles()
    {
        var initial = new ApplicationSettings
        {
            Port = ReservePort(),
            Source = SourceSelectionSettings.WindowsMedia("Player.App!Exact"),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App!Exact" },
            Spotify = new SpotifyConnectionSettings { ClientId = "client-id" },
        };
        await using (var disconnectedHost = await ControllerTestHost.StartAsync(initial))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                disconnectedHost.Controller.ApplySettingsAsync(new SettingsDraft(
                    initial.Port,
                    SourceProvider.SpotifyApi,
                    SourceKey.SpotifyApi().InstanceId,
                    initial.Appearance,
                    initial.Outputs,
                    initial.WindowTitle)));
        }

        await using var connectedHost = await ControllerTestHost.StartAsync(
            initial,
            seedSpotifyCredential: true);
        await connectedHost.Controller.ApplySettingsAsync(new SettingsDraft(
            initial.Port,
            SourceProvider.SpotifyApi,
            SourceKey.SpotifyApi().InstanceId,
            initial.Appearance,
            initial.Outputs,
            initial.WindowTitle));
        Assert.Equal(
            SourceProvider.SpotifyApi,
            connectedHost.SettingsStore.Load().Settings.Source.Provider);

        await connectedHost.Controller.DisconnectSpotifyAsync();

        var disconnected = connectedHost.SettingsStore.Load().Settings;
        Assert.Equal(SourceProvider.WindowsMedia, disconnected.Source.Provider);
        Assert.Equal("Player.App!Exact", disconnected.Source.InstanceId);
        Assert.Null(disconnected.Spotify.ClientId);
    }

    [Fact]
    public async Task BrowserPlayerUsesOneConnectionCodeAndDoesNotRequireSpotify()
    {
        var settings = new ApplicationSettings { Port = ReservePort() };
        await using var host = await ControllerTestHost.StartAsync(settings);

        await host.Controller.ApplySettingsAsync(new SettingsDraft(
            settings.Port,
            SourceProvider.ExternalPush,
            SourceKey.ExternalPush().InstanceId,
            settings.Appearance,
            settings.Outputs,
            settings.WindowTitle));
        var first = host.Controller.GetBrowserPlayerConnectionCode();
        var rotated = host.Controller.RotateBrowserPlayerConnectionCode();

        Assert.StartsWith($"npo1:{settings.Port}:", first, StringComparison.Ordinal);
        Assert.StartsWith($"npo1:{settings.Port}:", rotated, StringComparison.Ordinal);
        Assert.NotEqual(first, rotated);
        Assert.Equal(
            "http://127.0.0.1:" + settings.Port + "/NowPlayingOverlay.user.js",
            host.Controller.BrowserProducerUrl);
        Assert.Equal(
            SourceProvider.ExternalPush,
            host.SettingsStore.Load().Settings.Source.Provider);
    }

    [Fact]
    public void LoopbackProbeRejectsAnOccupiedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.False(LoopbackPortProbe.IsAvailable(port));
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class ControllerTestHost : IAsyncDisposable
    {
        private ControllerTestHost(
            TemporaryDirectory directory,
            ApplicationPaths paths,
            ApplicationSettingsStore settingsStore,
            OverlayComposition composition)
        {
            Directory = directory;
            Paths = paths;
            SettingsStore = settingsStore;
            Composition = composition;
        }

        public TemporaryDirectory Directory { get; }

        public ApplicationPaths Paths { get; }

        public ApplicationSettingsStore SettingsStore { get; }

        public OverlayComposition Composition { get; }

        public TrayMenuController Controller => Composition.TrayController;

        public static async Task<ControllerTestHost> StartAsync(
            ApplicationSettings settings,
            bool persistInitialSettings = true,
            bool seedSpotifyCredential = false)
        {
            var directory = new TemporaryDirectory();
            try
            {
                var paths = new ApplicationPaths(directory.Path);
                if (seedSpotifyCredential)
                {
                    new SpotifyCredentialStore(paths.SpotifyCredentialsFilePath).Save(
                        new SpotifyStoredCredential(
                            new SpotifyClientId(settings.Spotify.ClientId!),
                            "refresh-token",
                            SpotifyAuthorizationRequest.RequiredScope));
                }

                var store = new ApplicationSettingsStore(
                    paths.SettingsFilePath,
                    paths.RootDirectory);
                if (persistInitialSettings)
                {
                    store.Save(settings);
                }

                var composition = OverlayCompositionRoot.Compose(
                    [],
                    settings,
                    store,
                    paths);
                await composition.Runtime.StartAsync();
                return new ControllerTestHost(directory, paths, store, composition);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Composition.Runtime.DisposeAsync();
            Directory.Dispose();
        }
    }
}
