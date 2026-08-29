using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
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
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var effectivePort = 13000;
        var controller = new TrayMenuController(
            () => effectivePort,
            new ApplicationSettingsStore(settingsPath),
            () => new HostStatus("Source Not Configured", IsFaulted: false),
            Path.Combine(directory.Path, "logs"),
            (port, persistPort, _) =>
            {
                persistPort();
                effectivePort = port;
                return Task.CompletedTask;
            });

        var unchanged = await controller.SavePortAsync(13000);
        var fileExistsAfterUnchanged = File.Exists(settingsPath);
        var changed = await controller.SavePortAsync(13001);

        Assert.Equal("http://127.0.0.1:13001/NowPlaying.html", controller.OverlayUrl);
        Assert.False(unchanged.Changed);
        Assert.False(fileExistsAfterUnchanged);
        Assert.True(changed.Changed);
        Assert.Equal("http://127.0.0.1:13001/NowPlaying.html", changed.OverlayUrl);
        Assert.Equal(13001, new ApplicationSettingsStore(settingsPath).Load().Settings.Port);
        Assert.Equal("Source Not Configured", controller.GetStatus().Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task RejectsInvalidPort(int port)
    {
        using var directory = new TemporaryDirectory();
        var controller = new TrayMenuController(
            () => HostOptions.DefaultPort,
            new ApplicationSettingsStore(Path.Combine(directory.Path, "settings.json")),
            () => new HostStatus("Ready", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidDataException>(() => controller.SavePortAsync(port));
    }

    [Fact]
    public async Task DoesNotPersistWhenRebindingFails()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var controller = new TrayMenuController(
            () => HostOptions.DefaultPort,
            new ApplicationSettingsStore(settingsPath),
            () => new HostStatus("Ready", IsFaulted: false),
            directory.Path,
            (_, _, _) => throw new InvalidOperationException("Port is unavailable."));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SavePortAsync(13000));

        Assert.Contains("unavailable", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(settingsPath));
    }

    [Fact]
    public async Task RefreshSourcesForwardsTheRequestedProvider()
    {
        using var directory = new TemporaryDirectory();
        SourceProvider? requestedProvider = null;
        var expected = new SourceDiscoveryResult(
            [SourceDescriptor.SpotifyApi()],
            SourceManagerState.Unconfigured);
        var controller = new TrayMenuController(
            () => HostOptions.DefaultPort,
            new ApplicationSettingsStore(Path.Combine(directory.Path, "settings.json")),
            () => new HostStatus("Ready", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask,
            refreshSources: (provider, _) =>
            {
                requestedProvider = provider;
                return Task.FromResult(expected);
            });

        var actual = await controller.RefreshSourcesAsync(SourceProvider.SpotifyApi);

        Assert.Equal(SourceProvider.SpotifyApi, requestedProvider);
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SavingPortPreservesTheExactSourceSelection()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Port = 13000,
            Source = SourceSelectionSettings.WindowsMedia("Player.App"),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App" },
        });
        var effectivePort = 13000;
        var controller = new TrayMenuController(
            () => effectivePort,
            store,
            () => new HostStatus("Windows Media: Playing", IsFaulted: false),
            directory.Path,
            (port, persistPort, _) =>
            {
                persistPort();
                effectivePort = port;
                return Task.CompletedTask;
            });

        await controller.SavePortAsync(13001);

        var saved = store.Load().Settings;
        Assert.Equal(13001, saved.Port);
        Assert.Equal("Player.App", saved.Source.InstanceId);
        Assert.Equal("Player.App", saved.WindowsMedia.LastInstanceId);
    }

    [Fact]
    public async Task SavingWindowTitleParserReconfiguresTheActiveSourceWithoutReselectingIt()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var target = new WindowTitleTargetSettings
        {
            ProcessName = "Player",
            ExecutablePath = @"C:\Apps\Player.exe",
            WindowClass = "PlayerWindow",
        };
        var original = new WindowTitleSettings { Target = target };
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Port = 13000,
            Source = SourceSelectionSettings.WindowTitle(target.InstanceId),
            WindowTitle = original,
        });
        WindowTitleSettings? applied = null;
        SourceSelectionSettings? reselected = null;
        var controller = new TrayMenuController(
            () => 13000,
            store,
            () => new HostStatus("Window Title: Playing", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask,
            getSourceState: () => new SourceManagerState(
                SourceDescriptor.WindowTitle(target.InstanceId, target.DisplayName),
                SourceStatus.Available,
                SourceStatusReason.None),
            selectSource: source => reselected = source,
            setWindowTitleSettings: settings => applied = settings);
        var updated = original with
        {
            ParseMode = WindowTitleParseMode.Split,
            Separator = " | ",
            LeftField = WindowTitleField.Title,
        };

        await controller.SaveSettingsAsync(
            13000,
            SourceProvider.WindowTitle,
            target.InstanceId,
            new AppearanceSettings(),
            windowTitle: updated);

        Assert.Equal(updated, applied);
        Assert.Equal(updated, store.Load().Settings.WindowTitle);
        Assert.Null(reselected);
    }

    [Fact]
    public async Task CombinedSettingsSavePersistsAndActivatesCustomAppearance()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings { Port = 13000 });
        SourceSelectionSettings? activatedSource = null;
        AppearanceSettings? activatedAppearance = null;
        OutputSettings? activatedOutputs = null;
        var controller = new TrayMenuController(
            () => 13000,
            store,
            () => new HostStatus("Source Not Configured", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask,
            selectSource: value => activatedSource = value,
            setAppearance: value => activatedAppearance = value,
            setOutputs: value => activatedOutputs = value);
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
                FilePath = Path.Combine(directory.Path, "state.json"),
            },
        };

        var result = await controller.SaveSettingsAsync(
            13000,
            SourceProvider.WindowsMedia,
            "Player.App!Exact",
            appearance,
            outputs);

        var saved = store.Load().Settings;
        Assert.False(result.PortChanged);
        Assert.Equal("Player.App!Exact", saved.Source.InstanceId);
        Assert.Equal("Player.App!Exact", saved.WindowsMedia.LastInstanceId);
        Assert.Equal(appearance, saved.Appearance);
        Assert.True(saved.Outputs.Json.Enabled);
        Assert.Equal(outputs.Json.FilePath, saved.Outputs.Json.FilePath);
        Assert.Equal(SourceProvider.WindowsMedia, activatedSource?.Provider);
        Assert.Equal("Player.App!Exact", activatedSource?.InstanceId);
        Assert.Equal(appearance, activatedAppearance);
        Assert.Equal(outputs, activatedOutputs);
    }

    [Fact]
    public async Task SpotifyConnectionAndProviderSelectionHaveSeparateLifecycles()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Port = 13000,
            Source = SourceSelectionSettings.WindowsMedia("Player.App!Exact"),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App!Exact" },
        });
        var connection = new SpotifyConnectionState(SpotifyConnectionStatus.Disconnected);
        SourceSelectionSettings? activatedSource = null;
        var controller = new TrayMenuController(
            () => 13000,
            store,
            () => new HostStatus("Windows Media: Playing", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask,
            getSourceState: () => new SourceManagerState(
                SourceDescriptor.WindowsMedia("Player.App!Exact"),
                SourceStatus.Available,
                SourceStatusReason.None),
            selectSource: value => activatedSource = value,
            getSpotifyConnectionState: _ => connection,
            authorizeSpotify: (clientId, _, _) =>
            {
                connection = new SpotifyConnectionState(
                    SpotifyConnectionStatus.Connected,
                    clientId);
                return Task.FromResult(connection);
            },
            disconnectSpotify: _ =>
            {
                connection = new SpotifyConnectionState(SpotifyConnectionStatus.Disconnected);
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<InvalidDataException>(() => controller.SaveSettingsAsync(
            13000,
            SourceProvider.SpotifyApi,
            "current-account",
            new AppearanceSettings()));
        await controller.AuthorizeSpotifyAsync("client-id", reauthorize: false);

        var connected = store.Load().Settings;
        Assert.Equal(SourceProvider.WindowsMedia, connected.Source.Provider);
        Assert.Equal("client-id", connected.Spotify.ClientId);

        await controller.SaveSettingsAsync(
            13000,
            SourceProvider.SpotifyApi,
            "current-account",
            new AppearanceSettings());
        var selectedSpotify = store.Load().Settings;
        Assert.Equal(SourceProvider.SpotifyApi, selectedSpotify.Source.Provider);
        Assert.Equal("current-account", selectedSpotify.Source.InstanceId);
        Assert.Equal("Player.App!Exact", selectedSpotify.WindowsMedia.LastInstanceId);

        await controller.DisconnectSpotifyAsync();

        var disconnected = store.Load().Settings;
        Assert.Equal(SourceProvider.WindowsMedia, disconnected.Source.Provider);
        Assert.Equal("Player.App!Exact", disconnected.Source.InstanceId);
        Assert.Equal("Player.App!Exact", disconnected.WindowsMedia.LastInstanceId);
        Assert.Null(disconnected.Spotify.ClientId);
        Assert.Equal(SourceProvider.WindowsMedia, activatedSource?.Provider);
        Assert.Equal("Player.App!Exact", activatedSource?.InstanceId);
    }

    [Fact]
    public async Task BrowserPlayerUsesOneConnectionCodeAndDoesNotRequireSpotify()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        var firstKey = new string('A', IngestKey.EncodedLength);
        var rotatedKey = new string('B', IngestKey.EncodedLength);
        SourceSelectionSettings? activatedSource = null;
        var controller = new TrayMenuController(
            () => 14567,
            store,
            () => new HostStatus("Browser Player: Selected Player Not Available", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask,
            selectSource: source => activatedSource = source,
            exportIngestKey: () => firstKey,
            rotateIngestKey: () => rotatedKey);

        await controller.SaveSettingsAsync(
            14567,
            SourceProvider.ExternalPush,
            SourceKey.ExternalPush().InstanceId,
            new AppearanceSettings());

        Assert.Equal($"npo1:14567:{firstKey}", controller.GetBrowserPlayerConnectionCode());
        Assert.Equal($"npo1:14567:{rotatedKey}", controller.RotateBrowserPlayerConnectionCode());
        Assert.Equal(
            "http://127.0.0.1:14567/NowPlayingOverlay.user.js",
            controller.BrowserProducerUrl);
        Assert.Equal(SourceProvider.ExternalPush, store.Load().Settings.Source.Provider);
        Assert.Equal(SourceProvider.ExternalPush, activatedSource!.Provider);
        Assert.Equal(SourceKey.ExternalPush().InstanceId, activatedSource.InstanceId);
    }

    [Fact]
    public void LoopbackProbeRejectsAnOccupiedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.False(LoopbackPortProbe.IsAvailable(port));
    }
}
