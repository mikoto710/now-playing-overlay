using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.ControlPlane;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.ControlPlane;

public sealed class SettingsApplicationWorkflowTests
{
    [Fact]
    public async Task ApplyPersistsOneCompleteCandidateAndUpdatesRuntimeServices()
    {
        var initialPort = ReservePort();
        await using var host = await ControlPlaneHost.StartAsync(new ApplicationSettings
        {
            Port = initialPort,
        });
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

        var result = await host.Composition.Settings.ApplyAsync(
            new SettingsDraft(
                initialPort,
                SourceProvider.WindowsMedia,
                "Player.App!Exact",
                appearance,
                expectedOutputs,
                new WindowTitleSettings()));

        var saved = host.SettingsStore.Load().Settings;
        Assert.False(result.PortChanged);
        Assert.Equal("Player.App!Exact", saved.Source.InstanceId);
        Assert.Equal("Player.App!Exact", saved.WindowsMedia.LastInstanceId);
        Assert.Equal(appearance, saved.Appearance);
        Assert.Equal(expectedOutputs, saved.Outputs);
        Assert.Equal(
            SourceKey.WindowsMedia("Player.App!Exact"),
            host.Composition.Sources.GetState().ActiveSource!.Key);
    }

    [Fact]
    public async Task PortRebindProvesCandidateBeforePersistingAndPreservesOtherSettings()
    {
        var initialPort = ReservePort();
        var initial = new ApplicationSettings
        {
            Port = initialPort,
            Source = SourceSelectionSettings.ExternalPush(),
        };
        await using var host = await ControlPlaneHost.StartAsync(initial);
        var newPort = ReservePort();

        var result = await host.Composition.Settings.ApplyAsync(
            CreateDraft(initial with { Port = newPort }));

        var saved = host.SettingsStore.Load().Settings;
        Assert.True(result.PortChanged);
        Assert.Equal(newPort, host.Composition.Runtime.CurrentPort);
        Assert.Equal(newPort, saved.Port);
        Assert.Equal(SourceProvider.ExternalPush, saved.Source.Provider);
        using var client = CreateClient(newPort);
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task FailedPortPreparationLeavesOldRuntimeAndPersistedSettingsAuthoritative()
    {
        var initialPort = ReservePort();
        var initial = new ApplicationSettings { Port = initialPort };
        await using var host = await ControlPlaneHost.StartAsync(initial);
        var before = host.SettingsStore.Load().Settings;
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var unavailablePort = ((IPEndPoint)occupied.LocalEndpoint).Port;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.Composition.Settings.ApplyAsync(
                CreateDraft(initial with { Port = unavailablePort })));

        Assert.Equal(initialPort, host.Composition.Runtime.CurrentPort);
        Assert.Equal(before, host.SettingsStore.Load().Settings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task InvalidCandidateIsRejectedBeforePersistence(int port)
    {
        var initialPort = ReservePort();
        var initial = new ApplicationSettings { Port = initialPort };
        await using var host = await ControlPlaneHost.StartAsync(initial);
        var before = host.SettingsStore.Load().Settings;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            host.Composition.Settings.ApplyAsync(
                CreateDraft(initial with { Port = port })));

        Assert.Equal(before, host.SettingsStore.Load().Settings);
        Assert.Equal(initialPort, host.Composition.Runtime.CurrentPort);
    }

    [Fact]
    public async Task WindowTitleParserUpdateKeepsTheExistingSourceIdentity()
    {
        var port = ReservePort();
        var target = new WindowTitleTargetSettings
        {
            ProcessName = "Player",
            ExecutablePath = @"C:\Apps\Player.exe",
            WindowClass = "PlayerWindow",
        };
        var initial = new ApplicationSettings
        {
            Port = port,
            Source = SourceSelectionSettings.WindowTitle(target.InstanceId),
            WindowTitle = new WindowTitleSettings { Target = target },
        };
        await using var host = await ControlPlaneHost.StartAsync(initial);
        var beforeKey = host.Composition.Sources.GetState().ActiveSource!.Key;
        var updated = initial.WindowTitle with
        {
            ParseMode = WindowTitleParseMode.Split,
            Separator = " | ",
            LeftField = WindowTitleField.Title,
        };

        await host.Composition.Settings.ApplyAsync(
            new SettingsDraft(
                port,
                SourceProvider.WindowTitle,
                target.InstanceId,
                initial.Appearance,
                initial.Outputs,
                updated));

        Assert.Equal(updated, host.SettingsStore.Load().Settings.WindowTitle);
        Assert.Equal(beforeKey, host.Composition.Sources.GetState().ActiveSource!.Key);
    }

    [Fact]
    public async Task SpotifySelectionRequiresAnExistingConnection()
    {
        var port = ReservePort();
        var initial = new ApplicationSettings
        {
            Port = port,
            Source = SourceSelectionSettings.WindowsMedia("Player.App"),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App" },
            Spotify = new SpotifyConnectionSettings { ClientId = "client-id" },
        };
        await using var host = await ControlPlaneHost.StartAsync(initial);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            host.Composition.Settings.ApplyAsync(
                new SettingsDraft(
                    port,
                    SourceProvider.SpotifyApi,
                    SourceKey.SpotifyApi().InstanceId,
                    initial.Appearance,
                    initial.Outputs,
                    initial.WindowTitle)));

        Assert.Equal(SourceProvider.WindowsMedia, host.SettingsStore.Load().Settings.Source.Provider);
    }

    private static SettingsDraft CreateDraft(ApplicationSettings settings)
    {
        return new SettingsDraft(
            settings.Port,
            settings.Source.Provider,
            settings.Source.InstanceId,
            settings.Appearance,
            settings.Outputs,
            settings.WindowTitle);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HttpClient CreateClient(int port)
    {
        return new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    private sealed class ControlPlaneHost : IAsyncDisposable
    {
        private ControlPlaneHost(
            TemporaryDirectory directory,
            ApplicationSettingsStore settingsStore,
            OverlayComposition composition)
        {
            Directory = directory;
            SettingsStore = settingsStore;
            Composition = composition;
        }

        public TemporaryDirectory Directory { get; }

        public ApplicationSettingsStore SettingsStore { get; }

        public OverlayComposition Composition { get; }

        public static async Task<ControlPlaneHost> StartAsync(ApplicationSettings settings)
        {
            var directory = new TemporaryDirectory();
            try
            {
                var paths = new ApplicationPaths(directory.Path);
                var store = new ApplicationSettingsStore(
                    paths.SettingsFilePath,
                    paths.RootDirectory);
                store.Save(settings);
                var composition = OverlayCompositionRoot.Compose(
                    [],
                    settings,
                    store,
                    paths);
                await composition.Runtime.StartAsync();
                return new ControlPlaneHost(directory, store, composition);
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
