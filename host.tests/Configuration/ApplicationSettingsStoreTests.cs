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
        Assert.Equal(AppearancePreset.Default, result.Settings.Appearance.Preset);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtistColor,
            result.Settings.Appearance.Custom.ArtistColor);
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
    public void SaveAndLoadRoundTripsExactSourceAndCustomAppearance()
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
            Appearance = new AppearanceSettings
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
                },
            },
        });
        var result = store.Load();

        Assert.Equal("Player.App!Exact", result.Settings.Source.SourceAppUserModelId);
        Assert.Equal(AppearancePreset.Custom, result.Settings.Appearance.Preset);
        Assert.Equal("#123456", result.Settings.Appearance.Custom.ArtistColor);
        Assert.Equal(65, result.Settings.Appearance.Custom.BackgroundOpacityPercent);
        Assert.Equal(12, result.Settings.Appearance.Custom.CornerRadius);
        Assert.Equal("Segoe UI", result.Settings.Appearance.Custom.FontFamily);
        Assert.Equal(18, result.Settings.Appearance.Custom.ArtistFontSize);
        Assert.Equal(500, result.Settings.Appearance.Custom.ArtistFontWeight);
        Assert.Equal(24, result.Settings.Appearance.Custom.TrackFontSize);
        Assert.Equal(600, result.Settings.Appearance.Custom.TrackFontWeight);
        var json = File.ReadAllText(path);
        Assert.Contains("\"provider\": \"windows-media\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceAppUserModelId\": \"Player.App!Exact\"", json, StringComparison.Ordinal);
        Assert.Contains("\"preset\": \"custom\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleOneCustomAppearanceKeepsColorsAndReceivesTypographyDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "port": 13130,
              "source": {
                "provider": "windows-media",
                "sourceAppUserModelId": "Player.App!Exact"
              },
              "appearance": {
                "preset": "custom",
                "custom": {
                  "artistColor": "#123456",
                  "trackColor": "#ABCDEF",
                  "backgroundColor": "#102030",
                  "backgroundOpacityPercent": 65,
                  "cornerRadius": 12
                }
              }
            }
            """);
        var store = new ApplicationSettingsStore(path);

        var result = store.Load();

        Assert.Equal(AppearancePreset.Custom, result.Settings.Appearance.Preset);
        Assert.Equal("#123456", result.Settings.Appearance.Custom.ArtistColor);
        Assert.Null(result.Settings.Appearance.Custom.FontFamily);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtistFontSize,
            result.Settings.Appearance.Custom.ArtistFontSize);
        Assert.Equal(
            CustomAppearanceSettings.DefaultTrackFontWeight,
            result.Settings.Appearance.Custom.TrackFontWeight);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void InvalidAppearanceFallsBackWithoutDiscardingCoreSettings()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "port": 13130,
              "source": {
                "provider": "windows-media",
                "sourceAppUserModelId": "Player.App!Exact"
              },
              "appearance": {
                "preset": "custom",
                "custom": {
                  "artistColor": "#123456",
                  "trackColor": "#ABCDEF",
                  "backgroundColor": "#102030",
                  "backgroundOpacityPercent": 65,
                  "cornerRadius": 99
                }
              }
            }
            """);
        var store = new ApplicationSettingsStore(path);

        var result = store.Load();

        Assert.Equal(13130, result.Settings.Port);
        Assert.Equal("Player.App!Exact", result.Settings.Source.SourceAppUserModelId);
        Assert.Equal(AppearancePreset.Default, result.Settings.Appearance.Preset);
        Assert.Equal(
            CustomAppearanceSettings.DefaultBackgroundColor,
            result.Settings.Appearance.Custom.BackgroundColor);
        Assert.Contains("default appearance", result.Warning, StringComparison.Ordinal);
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
