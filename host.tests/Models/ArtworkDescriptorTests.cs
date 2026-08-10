using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Models;

public sealed class ArtworkDescriptorTests
{
    private const string ArtworkId =
        "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a";

    [Fact]
    public void CreateCanonicalizesSupportedContentType()
    {
        var artwork = ArtworkDescriptor.Create(1, ArtworkId, "IMAGE/PNG", 1024);

        Assert.Equal("image/png", artwork.ContentType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateRejectsNonPositiveRevision(long revision)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArtworkDescriptor.Create(revision, ArtworkId, "image/png", 1024));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF")]
    [InlineData("9F64A747E1B97F131FABB6B447296C9B6F0201E79FB3C5356E6C77E89B6A806A")]
    public void CreateRejectsInvalidArtworkId(string artworkId)
    {
        Assert.Throws<ArgumentException>(
            () => ArtworkDescriptor.Create(1, artworkId, "image/png", 1024));
    }

    [Fact]
    public void CreateRejectsUnsupportedContentType()
    {
        Assert.Throws<ArgumentException>(
            () => ArtworkDescriptor.Create(1, ArtworkId, "image/gif", 1024));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ArtworkDescriptor.MaximumByteLength + 1)]
    public void CreateRejectsByteLengthOutsideLimit(int byteLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArtworkDescriptor.Create(1, ArtworkId, "image/png", byteLength));
    }
}
