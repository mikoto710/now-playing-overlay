using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class ApplicationSettingsStoreTests
{
    [Fact]
    public void MissingSettingsUseDocumentedDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new ApplicationSettingsStore(Path.Combine(directory.Path, "settings.json"));

        var result = store.Load();

        Assert.Equal(HostOptions.DefaultPort, result.Settings.Port);
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

    [Theory]
    [InlineData("{\"port\":0}")]
    [InlineData("{\"port\":10598,\"unexpected\":true}")]
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("now-playing-overlay-settings-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
