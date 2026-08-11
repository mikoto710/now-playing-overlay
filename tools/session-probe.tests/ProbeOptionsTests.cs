namespace NowPlayingOverlay.SessionProbe.Tests;

public sealed class ProbeOptionsTests
{
    [Fact]
    public void ParseReadsAllSupportedOptions()
    {
        var options = ProbeOptions.Parse(
            ["--duration", "12.5", "--output", "evidence.jsonl", "--exercise-source", "Spotify.exe"]);

        Assert.Equal(TimeSpan.FromSeconds(12.5), options.Duration);
        Assert.Equal(Path.GetFullPath("evidence.jsonl"), options.OutputPath);
        Assert.Equal("Spotify.exe", options.ExerciseSource);
        Assert.False(options.ShowHelp);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void ParseRejectsInvalidDuration(string value)
    {
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(["--duration", value]));
    }

    [Fact]
    public void ParseRejectsUnknownArguments()
    {
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(["--unknown"]));
    }
}
