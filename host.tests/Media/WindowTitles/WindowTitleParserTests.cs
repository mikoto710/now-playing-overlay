using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.WindowTitles;

namespace NowPlayingOverlay.Host.Tests.Media.WindowTitles;

public sealed class WindowTitleParserTests
{
    [Fact]
    public void WholeTitleDoesNotGuessArtistOrder()
    {
        var parsed = WindowTitleParser.Parse(
            "Song - Artist",
            new WindowTitleSettings());

        Assert.Equal("Song - Artist", parsed.Title);
        Assert.Equal(string.Empty, parsed.Artist);
    }

    [Fact]
    public void SplitUsesTheExplicitSeparatorAndLeftField()
    {
        var parsed = WindowTitleParser.Parse(
            "Artist | Song",
            new WindowTitleSettings
            {
                ParseMode = WindowTitleParseMode.Split,
                Separator = " | ",
                LeftField = WindowTitleField.Artist,
            });

        Assert.Equal("Song", parsed.Title);
        Assert.Equal("Artist", parsed.Artist);
    }

    [Fact]
    public void LastOccurrenceKeepsEarlierSeparatorsInTheLeftSide()
    {
        var parsed = WindowTitleParser.Parse(
            "Song - Live - Artist",
            new WindowTitleSettings
            {
                ParseMode = WindowTitleParseMode.Split,
                Separator = " - ",
                SplitOccurrence = WindowTitleSplitOccurrence.Last,
                LeftField = WindowTitleField.Title,
            });

        Assert.Equal("Song - Live", parsed.Title);
        Assert.Equal("Artist", parsed.Artist);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Player")]
    [InlineData(" - Artist")]
    [InlineData("Song - ")]
    public void SplitWithoutTwoUsableSidesDoesNotPublishATrack(string title)
    {
        var parsed = WindowTitleParser.Parse(
            title,
            new WindowTitleSettings
            {
                ParseMode = WindowTitleParseMode.Split,
                Separator = " - ",
                LeftField = WindowTitleField.Title,
            });

        Assert.False(parsed.HasTrack);
    }

    [Fact]
    public void TargetIdentityIgnoresWindowsPathAndProcessNameCasing()
    {
        var first = Target("Player", @"C:\Apps\Player.exe", "PlayerWindow");
        var second = Target("player", @"c:\apps\player.exe", "PlayerWindow");

        Assert.Equal(first.InstanceId, second.InstanceId);
        Assert.Equal(64, first.InstanceId.Length);
    }

    private static WindowTitleTargetSettings Target(
        string processName,
        string path,
        string windowClass)
    {
        return new WindowTitleTargetSettings
        {
            ProcessName = processName,
            ExecutablePath = path,
            WindowClass = windowClass,
        };
    }
}
