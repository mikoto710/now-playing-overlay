using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Diagnostics;

public sealed class BoundedLogFileTests
{
    [Fact]
    public void NamedAndTypedLoggersWriteTheirCategoriesAndFilterNone()
    {
        using var directory = new TemporaryDirectory("now-playing-overlay-log-tests-");
        var path = Path.Combine(directory.Path, "NowPlayingOverlay.log");
        using (var log = new BoundedLogFile(path))
        using (var provider = new BoundedFileLoggerProvider(log))
        {
            var named = provider.CreateLogger("Playback");
            var typed = provider.CreateLogger<BoundedLogFileTests>();
            named.LogInformation(new EventId(17), "Reading {Source}", "Spotify");
            typed.LogWarning(new EventId(23), "Retry in {Seconds}s", 5);
            named.Log(LogLevel.None, "Suppressed named entry");
            typed.Log(LogLevel.None, "Suppressed typed entry");
        }

        Assert.Collection(File.ReadAllLines(path),
            line => Assert.EndsWith(" INFORMATION Playback[17] - Reading Spotify", line, StringComparison.Ordinal),
            line => Assert.EndsWith($" WARNING {typeof(BoundedLogFileTests).FullName}[23] - Retry in 5s", line, StringComparison.Ordinal));
    }

    [Fact]
    public void RotationKeepsFileCountAndTotalBytesBounded()
    {
        using var directory = new TemporaryDirectory("now-playing-overlay-log-tests-");
        var path = Path.Combine(directory.Path, "NowPlayingOverlay.log");

        using (var log = new BoundedLogFile(path, maximumFileBytes: 256, maximumFileCount: 3))
        {
            for (var index = 0; index < 30; index++)
            {
                log.Write(LogLevel.Information, "Test", default, $"Entry {index}: {new string('x', 80)}");
            }
        }

        var files = Directory.GetFiles(directory.Path, "NowPlayingOverlay*.log");
        Assert.InRange(files.Length, 1, 3);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 256));
        Assert.InRange(files.Sum(file => new FileInfo(file).Length), 1, 256 * 3);
    }

    [Fact]
    public void OversizedEntryIsTruncatedWithoutBreakingUtf8Boundary()
    {
        using var directory = new TemporaryDirectory("now-playing-overlay-log-tests-");
        var path = Path.Combine(directory.Path, "NowPlayingOverlay.log");

        using (var log = new BoundedLogFile(path, maximumFileBytes: 160, maximumFileCount: 1))
        {
            log.Write(LogLevel.Error, "Test", default, string.Concat(Enumerable.Repeat("音乐", 100)));
        }

        var bytes = File.ReadAllBytes(path);
        var text = File.ReadAllText(path);
        Assert.InRange(bytes.Length, 1, 160);
        Assert.EndsWith("... log entry truncated\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', text);
    }
}
