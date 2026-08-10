using System.Net;
using System.Net.Sockets;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class TrayMenuControllerTests
{
    [Fact]
    public void UsesEffectivePortAndPersistsOnlyAChangedPort()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var controller = new TrayMenuController(
            new HostOptions { Port = 13000 },
            new ApplicationSettingsStore(settingsPath),
            () => new TrayStatus("Waiting for Spotify", IsFaulted: false),
            Path.Combine(directory.Path, "logs"),
            _ => true);

        var unchanged = controller.SavePort(13000);
        var fileExistsAfterUnchanged = File.Exists(settingsPath);
        var changed = controller.SavePort(13001);

        Assert.Equal("http://127.0.0.1:13000/NowPlaying.html", controller.OverlayUrl);
        Assert.False(unchanged.Changed);
        Assert.False(fileExistsAfterUnchanged);
        Assert.True(changed.Changed);
        Assert.True(changed.RequiresRestart);
        Assert.Equal("http://127.0.0.1:13001/NowPlaying.html", changed.OverlayUrl);
        Assert.Equal(13001, new ApplicationSettingsStore(settingsPath).Load().Settings.Port);
        Assert.Equal("Waiting for Spotify", controller.GetStatus().Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsInvalidPort(int port)
    {
        using var directory = new TemporaryDirectory();
        var controller = new TrayMenuController(
            new HostOptions(),
            new ApplicationSettingsStore(Path.Combine(directory.Path, "settings.json")),
            () => new TrayStatus("Ready", IsFaulted: false),
            directory.Path,
            _ => true);

        Assert.Throws<InvalidDataException>(() => controller.SavePort(port));
    }

    [Fact]
    public void DoesNotPersistUnavailablePort()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var controller = new TrayMenuController(
            new HostOptions(),
            new ApplicationSettingsStore(settingsPath),
            () => new TrayStatus("Ready", IsFaulted: false),
            directory.Path,
            _ => false);

        var error = Assert.Throws<InvalidOperationException>(() => controller.SavePort(13000));

        Assert.Contains("not available", error.Message, StringComparison.Ordinal);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("now-playing-overlay-tray-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
