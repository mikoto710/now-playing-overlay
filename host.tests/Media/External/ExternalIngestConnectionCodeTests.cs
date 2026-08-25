using NowPlayingOverlay.Host.Media.External;

namespace NowPlayingOverlay.Host.Tests.Media.External;

public sealed class ExternalIngestConnectionCodeTests
{
    [Fact]
    public void CreatesAnOpaqueVersionedCodeWithoutAUrl()
    {
        var key = new string('A', IngestKey.EncodedLength);

        var code = ExternalIngestConnectionCode.Create(13130, key);

        Assert.Equal($"npo1:13130:{key}", code);
        Assert.DoesNotContain("127.0.0.1", code, StringComparison.Ordinal);
        Assert.DoesNotContain("http", code, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(65536, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(13130, "too-short")]
    [InlineData(13130, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void RejectsInvalidPortsAndNonCanonicalKeys(int port, string key)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ExternalIngestConnectionCode.Create(port, key));
    }
}
