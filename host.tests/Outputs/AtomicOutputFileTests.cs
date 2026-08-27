using System.Text;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Outputs;

public sealed class AtomicOutputFileTests
{
    [Fact]
    public async Task ReplacesTargetWithUtf8WithoutBom()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "now-playing.txt");
        await File.WriteAllTextAsync(path, "old");
        var writer = new AtomicOutputFile();

        await writer.WriteTextAsync(path, "歌曲 😀", CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal("歌曲 😀", Encoding.UTF8.GetString(bytes));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task LockedTargetFailsWithoutChangingOriginalOrLeavingTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "now-playing.txt");
        await File.WriteAllTextAsync(path, "old");
        await using var locked = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var writer = new AtomicOutputFile();

        var error = await Record.ExceptionAsync(() =>
            writer.WriteTextAsync(path, "new", CancellationToken.None));

        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("old", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }
}
