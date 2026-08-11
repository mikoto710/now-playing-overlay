using System.Text;
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
}
