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

        Assert.Empty(settings.Text);
        Assert.False(settings.Json.Enabled);
        Assert.False(settings.Artwork.Enabled);
        Assert.False(settings.History.Enabled);
    }

    [Fact]
    public void EnabledTargetsRequireAbsoluteExistingDirectoriesAndExpectedExtensions()
    {
        Assert.Throws<InvalidDataException>(() => new OutputSettings
        {
            Text =
            [
                new TextOutputSettings
                {
                    Enabled = true,
                    FilePath = "relative.txt",
                },
            ],
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
            Text =
            [
                new TextOutputSettings
                {
                    Enabled = true,
                    Name = "Current",
                    FilePath = path,
                },
            ],
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
