using System.Text;
using NowPlayingOverlay.Host.Hosting;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class BrowserProducerAssetTests
{
    [Fact]
    public void EmbeddedAssetIsAStandaloneBoundedUserscript()
    {
        var asset = BrowserProducerAsset.LoadEmbedded(typeof(BrowserProducerAsset).Assembly);
        var script = Encoding.UTF8.GetString(asset.Bytes.Span);

        Assert.Contains("// ==UserScript==", script, StringComparison.Ordinal);
        Assert.Contains("// @connect      127.0.0.1", script, StringComparison.Ordinal);
        Assert.Contains("// @connect      scdn.co", script, StringComparison.Ordinal);
        Assert.Contains("// @connect      ytimg.com", script, StringComparison.Ordinal);
        Assert.DoesNotContain("// @connect      *", script, StringComparison.Ordinal);
        Assert.Contains("navigator.mediaSession", script, StringComparison.Ordinal);
        Assert.Contains("/ingest/v1/state", script, StringComparison.Ordinal);
        Assert.Contains("/ingest/v1/heartbeat", script, StringComparison.Ordinal);
        Assert.Contains("/ingest/v1/artwork", script, StringComparison.Ordinal);
        Assert.DoesNotContain("@match        *://*/*", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer AAAAA", script, StringComparison.Ordinal);
    }
}
