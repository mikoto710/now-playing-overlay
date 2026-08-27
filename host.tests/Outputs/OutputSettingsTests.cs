using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Outputs;

public sealed class OutputSettingsTests
{
    [Fact]
    public void DefaultsAreValidAndDisabled()
    {
        var settings = new OutputSettings();

        settings.Validate();

        Assert.False(settings.Text.Enabled);
        Assert.False(settings.Json.Enabled);
        Assert.False(settings.Artwork.Enabled);
        Assert.False(settings.History.Enabled);
    }

    [Fact]
    public void DefaultFilePathsUseTheConfiguredDirectoryWithoutEnablingOutputs()
    {
        using var directory = new TemporaryDirectory();

        var settings = new OutputSettings().WithDefaultFilePaths(directory.Path);

        settings.Validate();
        Assert.False(settings.Text.Enabled);
        Assert.Equal(Path.Combine(directory.Path, "NowPlaying.txt"), settings.Text.FilePath);
        Assert.False(settings.Json.Enabled);
        Assert.Equal(Path.Combine(directory.Path, "NowPlaying.json"), settings.Json.FilePath);
        Assert.False(settings.Artwork.Enabled);
        Assert.Equal(Path.Combine(directory.Path, "Artwork.png"), settings.Artwork.FilePath);
        Assert.False(settings.History.Enabled);
        Assert.Equal(Path.Combine(directory.Path, "History.txt"), settings.History.FilePath);
    }

    [Fact]
    public void DefaultFilePathsDoNotReplaceCustomPaths()
    {
        using var directory = new TemporaryDirectory();
        var customPath = Path.Combine(directory.Path, "custom.txt");
        var settings = new OutputSettings
        {
            Text = new TextOutputSettings { FilePath = customPath },
        };

        var withDefaults = settings.WithDefaultFilePaths(
            Path.Combine(directory.Path, "defaults"));

        Assert.Equal(customPath, withDefaults.Text.FilePath);
    }

    [Fact]
    public void EnabledTargetsRequireAbsoluteExistingDirectoriesAndExpectedExtensions()
    {
        Assert.Throws<InvalidDataException>(() => new OutputSettings
        {
            Text = new TextOutputSettings
            {
                Enabled = true,
                FilePath = "relative.txt",
            },
        }.Validate());

        using var directory = new TemporaryDirectory();
        Assert.Throws<InvalidDataException>(() => new OutputSettings
        {
            Artwork = new ArtworkOutputSettings
            {
                Enabled = true,
                FilePath = Path.Combine(directory.Path, "cover.jpg"),
            },
        }.Validate());
    }

    [Fact]
    public void EnabledTargetsCannotShareAPath()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "now-playing.txt");
        var settings = new OutputSettings
        {
            Text = new TextOutputSettings
            {
                Enabled = true,
                FilePath = path,
            },
            History = new HistoryOutputSettings
            {
                Enabled = true,
                FilePath = path,
            },
        };

        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void HistoryTemplateMustRemainOneLine()
    {
        var settings = new HistoryOutputSettings { Template = "{title}{newline}" };

        Assert.Throws<InvalidDataException>(settings.Validate);
    }
}
