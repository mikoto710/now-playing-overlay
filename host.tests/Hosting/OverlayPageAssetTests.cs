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
        Assert.DoesNotContain("/api/v2/", html, StringComparison.Ordinal);
        Assert.Contains("/api/v3/state", html, StringComparison.Ordinal);
        Assert.Contains("/api/v3/events", html, StringComparison.Ordinal);
        Assert.Contains("/api/v3/appearance", html, StringComparison.Ordinal);
        Assert.Contains("/api/v3/artwork/", html, StringComparison.Ordinal);
    }
}
