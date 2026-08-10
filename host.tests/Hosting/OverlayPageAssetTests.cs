using System.Text;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class OverlayPageAssetTests
{
    [Fact]
    public void EmbeddedAssetContainsProductionOverlay()
    {
        var asset = OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly);
        var html = Encoding.UTF8.GetString(asset.Bytes.Span);

        Assert.Contains("id=\"now-playing\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol diagnostic", html, StringComparison.Ordinal);
        Assert.StartsWith("embedded resource", asset.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentModeReadsExplicitDistDirectory()
    {
        using var directory = new TemporaryDirectory();
        var dist = Directory.CreateDirectory(Path.Combine(directory.Path, "web", "dist"));
        var pagePath = Path.Combine(dist.FullName, "NowPlaying.html");
        File.WriteAllText(pagePath, "<html>development overlay</html>");
        var options = new HostOptions { WebAssetMode = WebAssetMode.Development };

        var asset = OverlayPageAsset.Load(options, directory.Path);

        Assert.Equal("<html>development overlay</html>", Encoding.UTF8.GetString(asset.Bytes.Span));
        Assert.Equal(pagePath, asset.Source);
    }

    [Fact]
    public void DevelopmentModeReportsMissingBuildWithResolvedPath()
    {
        using var directory = new TemporaryDirectory();
        var options = new HostOptions { WebAssetMode = WebAssetMode.Development };

        var error = Assert.Throws<FileNotFoundException>(() =>
            OverlayPageAsset.Load(options, directory.Path));

        Assert.Contains(Path.Combine("web", "dist", "NowPlaying.html"), error.Message, StringComparison.Ordinal);
        Assert.Contains("npm --prefix web run build", error.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("now-playing-overlay-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
