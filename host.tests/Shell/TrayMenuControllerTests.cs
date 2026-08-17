using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
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
            TrayMenuController.BuildOverlayPreviewUrl(13000, previewScale: 3));
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
    public async Task SavingPortPreservesTheExactSourceSelection()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Port = 13000,
            Source = new SourceSelectionSettings { SourceAppUserModelId = "Player.App" },
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
        Assert.Equal("Player.App", saved.Source.SourceAppUserModelId);
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
        var controller = new TrayMenuController(
            () => 13000,
            store,
            () => new HostStatus("Source Not Configured", IsFaulted: false),
            directory.Path,
            (_, _, _) => Task.CompletedTask,
            selectSource: value => activatedSource = value,
            setAppearance: value => activatedAppearance = value);
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

        var result = await controller.SaveSettingsAsync(
            13000,
            SourceProvider.WindowsMedia,
            "Player.App!Exact",
            appearance);

        var saved = store.Load().Settings;
        Assert.False(result.PortChanged);
        Assert.Equal("Player.App!Exact", saved.Source.SourceAppUserModelId);
        Assert.Equal(appearance, saved.Appearance);
        Assert.Equal(SourceProvider.WindowsMedia, activatedSource?.Provider);
        Assert.Equal("Player.App!Exact", activatedSource?.SourceAppUserModelId);
        Assert.Equal(appearance, activatedAppearance);
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
            Source = new SourceSelectionSettings
            {
                Provider = SourceProvider.WindowsMedia,
                SourceAppUserModelId = "Player.App!Exact",
            },
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
            "Player.App!Exact",
            new AppearanceSettings()));
        await controller.AuthorizeSpotifyAsync("client-id", reauthorize: false);

        var connected = store.Load().Settings;
        Assert.Equal(SourceProvider.WindowsMedia, connected.Source.Provider);
        Assert.Equal("client-id", connected.Spotify.ClientId);

        await controller.SaveSettingsAsync(
            13000,
            SourceProvider.SpotifyApi,
            "Player.App!Exact",
            new AppearanceSettings());
        Assert.Equal(SourceProvider.SpotifyApi, store.Load().Settings.Source.Provider);

        await controller.DisconnectSpotifyAsync();

        var disconnected = store.Load().Settings;
        Assert.Equal(SourceProvider.WindowsMedia, disconnected.Source.Provider);
        Assert.Equal("Player.App!Exact", disconnected.Source.SourceAppUserModelId);
        Assert.Null(disconnected.Spotify.ClientId);
        Assert.Equal(SourceProvider.WindowsMedia, activatedSource?.Provider);
        Assert.Equal("Player.App!Exact", activatedSource?.SourceAppUserModelId);
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
