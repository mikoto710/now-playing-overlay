using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Configuration;

public sealed class ApplicationSettingsStoreTests
{
    [Fact]
    public void MissingSettingsUseDocumentedDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new ApplicationSettingsStore(
            Path.Combine(directory.Path, "settings.json"),
            directory.Path);

        var result = store.Load();

        Assert.Equal(HostOptions.DefaultPort, result.Settings.Port);
        Assert.Equal(SourceProvider.WindowsMedia, result.Settings.Source.Provider);
        Assert.Null(result.Settings.Source.InstanceId);
        Assert.Null(result.Settings.WindowsMedia.LastInstanceId);
        Assert.Equal(AppearancePreset.Default, result.Settings.Appearance.Preset);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtistColor,
            result.Settings.Appearance.Custom.ArtistColor);
        Assert.False(result.Settings.Outputs.Text.Enabled);
        Assert.Equal(
            Path.Combine(directory.Path, "NowPlaying.txt"),
            result.Settings.Outputs.Text.FilePath);
        Assert.False(result.Settings.Outputs.Json.Enabled);
        Assert.Equal(
            Path.Combine(directory.Path, "NowPlaying.json"),
            result.Settings.Outputs.Json.FilePath);
        Assert.False(result.Settings.Outputs.Artwork.Enabled);
        Assert.Equal(
            Path.Combine(directory.Path, "Artwork.png"),
            result.Settings.Outputs.Artwork.FilePath);
        Assert.False(result.Settings.Outputs.History.Enabled);
        Assert.Equal(
            Path.Combine(directory.Path, "History.txt"),
            result.Settings.Outputs.History.FilePath);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void LegacyPortOnlySettingsRemainReadableWithUnconfiguredWindowsMedia()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{\"port\":10598}");
        var store = new ApplicationSettingsStore(path, directory.Path);

        var result = store.Load();

        Assert.Equal(10598, result.Settings.Port);
        Assert.Equal(SourceProvider.WindowsMedia, result.Settings.Source.Provider);
        Assert.Null(result.Settings.Source.InstanceId);
        Assert.Null(result.Settings.WindowsMedia.LastInstanceId);
        Assert.Equal(
            Path.Combine(directory.Path, "NowPlaying.txt"),
            result.Settings.Outputs.Text.FilePath);
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
    public void SaveAndLoadRoundTripsOutputSettings()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var textPath = Path.Combine(directory.Path, "now-playing.txt");
        var jsonPath = Path.Combine(directory.Path, "state.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Outputs = new OutputSettings
            {
                Text = new TextOutputSettings
                {
                    Enabled = true,
                    FilePath = textPath,
                    Template = "{artist} - {title}",
                    NoMediaBehavior = NoMediaOutputBehavior.KeepLast,
                },
                Json = new JsonOutputSettings
                {
                    Enabled = true,
                    FilePath = jsonPath,
                    Format = JsonOutputFormat.Indented,
                },
            },
        });

        var loaded = store.Load();

        Assert.Null(loaded.Warning);
        Assert.Equal(textPath, loaded.Settings.Outputs.Text.FilePath);
        Assert.Equal(
            NoMediaOutputBehavior.KeepLast,
            loaded.Settings.Outputs.Text.NoMediaBehavior);
        Assert.Equal(JsonOutputFormat.Indented, loaded.Settings.Outputs.Json.Format);
        var json = File.ReadAllText(path);
        Assert.Contains("\"text\": {", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"text\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"noMediaBehavior\": \"keep-last\"", json, StringComparison.Ordinal);
        Assert.Contains("\"format\": \"indented\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PrototypeTextOutputArrayMigratesToSingleDirectOutput()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var firstPath = Path.Combine(directory.Path, "now-playing.txt");
        var secondPath = Path.Combine(directory.Path, "title.txt");
        File.WriteAllText(
            path,
            $$"""
            {
              "outputs": {
                "text": [
                  {
                    "enabled": true,
                    "name": "Now Playing",
                    "filePath": "{{firstPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                    "template": "{nowPlaying}",
                    "noMediaBehavior": "clear",
                    "noMediaTemplate": ""
                  },
                  {
                    "enabled": true,
                    "name": "Title",
                    "filePath": "{{secondPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                    "template": "{title}",
                    "noMediaBehavior": "clear",
                    "noMediaTemplate": ""
                  }
                ],
                "json": {},
                "artwork": {},
                "history": {}
              }
            }
            """);
        var store = new ApplicationSettingsStore(path);

        var loaded = store.Load();

        Assert.True(loaded.Settings.Outputs.Text.Enabled);
        Assert.Equal(firstPath, loaded.Settings.Outputs.Text.FilePath);
        Assert.Equal("{nowPlaying}", loaded.Settings.Outputs.Text.Template);
        Assert.Contains("only the first was retained", loaded.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidOutputsAreDisabledWithoutDiscardingOtherSettings()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "port": 13001,
              "outputs": {
                "text": [],
                "json": {
                  "enabled": true,
                  "filePath": "relative.json",
                  "format": "compact"
                },
                "artwork": {},
                "history": {}
              }
            }
            """);
        var store = new ApplicationSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(13001, loaded.Settings.Port);
        Assert.False(loaded.Settings.Outputs.Json.Enabled);
        Assert.Contains("outputs will remain disabled", loaded.Warning, StringComparison.Ordinal);
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
                InstanceId = "Player.App!Exact",
            },
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App!Exact" },
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
                    ArtworkVisible = true,
                    ArtworkSize = 48,
                    ArtworkPosition = ArtworkPosition.Right,
                    ArtworkFit = ArtworkFit.Cover,
                    ArtworkCornerRadius = 8,
                },
            },
        });
        var result = store.Load();

        Assert.Equal("Player.App!Exact", result.Settings.Source.InstanceId);
        Assert.Equal("Player.App!Exact", result.Settings.WindowsMedia.LastInstanceId);
        Assert.Equal(AppearancePreset.Custom, result.Settings.Appearance.Preset);
        Assert.Equal("#123456", result.Settings.Appearance.Custom.ArtistColor);
        Assert.Equal(65, result.Settings.Appearance.Custom.BackgroundOpacityPercent);
        Assert.Equal(12, result.Settings.Appearance.Custom.CornerRadius);
        Assert.Equal("Segoe UI", result.Settings.Appearance.Custom.FontFamily);
        Assert.Equal(18, result.Settings.Appearance.Custom.ArtistFontSize);
        Assert.Equal(500, result.Settings.Appearance.Custom.ArtistFontWeight);
        Assert.Equal(24, result.Settings.Appearance.Custom.TrackFontSize);
        Assert.Equal(600, result.Settings.Appearance.Custom.TrackFontWeight);
        Assert.True(result.Settings.Appearance.Custom.ArtworkVisible);
        Assert.Equal(48, result.Settings.Appearance.Custom.ArtworkSize);
        Assert.Equal(ArtworkPosition.Right, result.Settings.Appearance.Custom.ArtworkPosition);
        Assert.Equal(ArtworkFit.Cover, result.Settings.Appearance.Custom.ArtworkFit);
        Assert.Equal(8, result.Settings.Appearance.Custom.ArtworkCornerRadius);
        var json = File.ReadAllText(path);
        Assert.Contains("\"provider\": \"windows-media\"", json, StringComparison.Ordinal);
        Assert.Contains("\"instanceId\": \"Player.App!Exact\"", json, StringComparison.Ordinal);
        Assert.Contains("\"lastInstanceId\": \"Player.App!Exact\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceAppUserModelId", json, StringComparison.Ordinal);
        Assert.Contains("\"preset\": \"custom\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndLoadRoundTripsSpotifySelectionAndDormantWindowsSource()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Source = SourceSelectionSettings.SpotifyApi(),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App!Exact" },
            Spotify = new SpotifyConnectionSettings { ClientId = "client-id" },
        });

        var saved = store.Load().Settings;

        Assert.Equal(SourceProvider.SpotifyApi, saved.Source.Provider);
        Assert.Equal("current-account", saved.Source.InstanceId);
        Assert.Equal("Player.App!Exact", saved.WindowsMedia.LastInstanceId);
        Assert.Equal("client-id", saved.Spotify.ClientId);
    }

    [Fact]
    public void SaveAndLoadRoundTripsFixedExternalPushSelection()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Source = SourceSelectionSettings.ExternalPush(),
            WindowsMedia = new WindowsMediaSettings { LastInstanceId = "Player.App!Exact" },
        });

        var saved = store.Load().Settings;

        Assert.Equal(SourceProvider.ExternalPush, saved.Source.Provider);
        Assert.Equal("default", saved.Source.InstanceId);
        Assert.Equal("Player.App!Exact", saved.WindowsMedia.LastInstanceId);
        Assert.Contains("\"provider\": \"external-push\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndLoadRoundTripsWindowTitleSelectionAndParser()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var target = new WindowTitleTargetSettings
        {
            ProcessName = "Player",
            ExecutablePath = @"C:\Apps\Player.exe",
            WindowClass = "PlayerWindow",
        };
        var windowTitle = new WindowTitleSettings
        {
            Target = target,
            ParseMode = WindowTitleParseMode.Split,
            Separator = " | ",
            SplitOccurrence = WindowTitleSplitOccurrence.Last,
            LeftField = WindowTitleField.Title,
        };
        var store = new ApplicationSettingsStore(path);
        store.Save(new ApplicationSettings
        {
            Source = SourceSelectionSettings.WindowTitle(target.InstanceId),
            WindowTitle = windowTitle,
        });

        var saved = store.Load().Settings;

        Assert.Equal(SourceProvider.WindowTitle, saved.Source.Provider);
        Assert.Equal(target.InstanceId, saved.Source.InstanceId);
        Assert.Equal(windowTitle, saved.WindowTitle);
        var json = File.ReadAllText(path);
        Assert.Contains("\"provider\": \"window-title\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parseMode\": \"split\"", json, StringComparison.Ordinal);
        Assert.Contains("\"splitOccurrence\": \"last\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySpotifySelectionMigratesDormantWindowsSource()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "source": {
                "provider": "spotify-api",
                "sourceAppUserModelId": "Player.App!Exact"
              },
              "spotify": {
                "clientId": "client-id"
              }
            }
            """);
        var store = new ApplicationSettingsStore(path);

        var result = store.Load();

        Assert.Equal(SourceProvider.SpotifyApi, result.Settings.Source.Provider);
        Assert.Equal("current-account", result.Settings.Source.InstanceId);
        Assert.Equal("Player.App!Exact", result.Settings.WindowsMedia.LastInstanceId);
        Assert.Equal("client-id", result.Settings.Spotify.ClientId);
        Assert.Null(result.Warning);
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
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtworkVisible,
            result.Settings.Appearance.Custom.ArtworkVisible);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtworkSize,
            result.Settings.Appearance.Custom.ArtworkSize);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtworkPosition,
            result.Settings.Appearance.Custom.ArtworkPosition);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtworkFit,
            result.Settings.Appearance.Custom.ArtworkFit);
        Assert.Equal(
            CustomAppearanceSettings.DefaultArtworkCornerRadius,
            result.Settings.Appearance.Custom.ArtworkCornerRadius);
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
        Assert.Equal("Player.App!Exact", result.Settings.Source.InstanceId);
        Assert.Equal("Player.App!Exact", result.Settings.WindowsMedia.LastInstanceId);
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
    [InlineData("{\"source\":{\"provider\":\"spotify-api\",\"instanceId\":\"Player.App\"}}")]
    [InlineData("{\"source\":{\"provider\":\"external-push\",\"instanceId\":\"other\"}}")]
    [InlineData("{\"source\":{\"provider\":\"windows-media\",\"instanceId\":\"A\",\"sourceAppUserModelId\":\"A\"}}")]
    [InlineData("{\"source\":{\"provider\":\"windows-media\",\"instanceId\":\"A\"},\"windowsMedia\":{\"lastInstanceId\":\"B\"}}")]
    [InlineData("{\"windowsMedia\":null}")]
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
    public void InvalidSettingsWarningDoesNotExposeTheSettingsPathOrDocumentContents()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings-with-private-path.json");
        const string privateValue = "private-window-title-or-token";
        File.WriteAllText(path, $"{{\"unexpected\":\"{privateValue}\"}}");
        var store = new ApplicationSettingsStore(path);

        var result = store.Load();

        Assert.NotNull(result.Warning);
        Assert.DoesNotContain(path, result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privateValue, result.Warning, StringComparison.Ordinal);
        Assert.Contains("Error type", result.Warning, StringComparison.Ordinal);
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
