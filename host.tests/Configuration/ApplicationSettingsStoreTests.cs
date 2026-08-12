using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Configuration;

public sealed class ApplicationSettingsStoreTests
{
    [Fact]
    public void MissingSettingsUseDocumentedDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new ApplicationSettingsStore(Path.Combine(directory.Path, "settings.json"));

        var result = store.Load();

        Assert.Equal(HostOptions.DefaultPort, result.Settings.Port);
        Assert.Equal(SourceProvider.WindowsMedia, result.Settings.Source.Provider);
        Assert.Null(result.Settings.Source.SourceAppUserModelId);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void LegacyPortOnlySettingsRemainReadableWithUnconfiguredWindowsMedia()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{\"port\":13130}");
        var store = new ApplicationSettingsStore(path);

        var result = store.Load();

        Assert.Equal(13130, result.Settings.Port);
        Assert.Equal(SourceProvider.WindowsMedia, result.Settings.Source.Provider);
        Assert.Null(result.Settings.Source.SourceAppUserModelId);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void SaveAndLoadRoundTripsPortAtomically()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);

        store.Save(new ApplicationSettings { Port = 13000 });
        var result = store.Load();

        Assert.Equal(13000, result.Settings.Port);
        Assert.Null(result.Warning);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("\"port\": 13000", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndLoadRoundTripsExactWindowsMediaSelection()
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
        var result = store.Load();

        Assert.Equal("Player.App!Exact", result.Settings.Source.SourceAppUserModelId);
        var json = File.ReadAllText(path);
        Assert.Contains("\"provider\": \"windows-media\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceAppUserModelId\": \"Player.App!Exact\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"port\":0}")]
    [InlineData("{\"port\":10598,\"unexpected\":true}")]
    [InlineData("{\"port\":10598,\"source\":null}")]
    [InlineData("not json")]
    public void InvalidSettingsFallBackWithWarning(string json)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, json);
        var store = new ApplicationSettingsStore(path);

        var result = store.Load();

        Assert.Equal(HostOptions.DefaultPort, result.Settings.Port);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void SaveRejectsInvalidPortWithoutWritingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);

        Assert.Throws<InvalidDataException>(() =>
            store.Save(new ApplicationSettings { Port = 65536 }));
        Assert.False(File.Exists(path));
    }
}
