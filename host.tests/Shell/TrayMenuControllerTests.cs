using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Shell;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class TrayMenuControllerTests
{
    [Fact]
    public async Task UsesLiveEffectivePortAndPersistsOnlyAChangedPort()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var effectivePort = 13000;
        var controller = new TrayMenuController(
            () => effectivePort,
            new ApplicationSettingsStore(settingsPath),
            () => new HostStatus("Waiting for Spotify", IsFaulted: false),
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
        Assert.Equal("Waiting for Spotify", controller.GetStatus().Text);
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
    public void LoopbackProbeRejectsAnOccupiedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.False(LoopbackPortProbe.IsAvailable(port));
    }
}
