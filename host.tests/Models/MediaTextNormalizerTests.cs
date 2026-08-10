using System.Text;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Models;

public sealed class MediaTextNormalizerTests
{
    [Fact]
    public void NormalizeUsesCanonicalComposition()
    {
        var result = MediaTextNormalizer.Normalize("Cafe\u0301");

        Assert.Equal("Café", result);
        Assert.True(result.IsNormalized(NormalizationForm.FormC));
    }

    [Fact]
    public void NormalizeCollapsesConsecutiveLineSeparatorsAndTrims()
    {
        var result = MediaTextNormalizer.Normalize(" \r\nTitle\u0085\u2028Artist\u2029 ");

        Assert.Equal("Title Artist", result);
    }

    [Fact]
    public void NormalizeLimitsUnicodeScalarsWithoutSplittingSurrogates()
    {
        var result = MediaTextNormalizer.Normalize(string.Concat(Enumerable.Repeat("😀", 513)));

        Assert.Equal(MediaTextNormalizer.MaximumScalarCount, result.EnumerateRunes().Count());
        Assert.EndsWith("😀", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeReplacesInvalidUtf16WithReplacementCharacter()
    {
        Assert.Equal("a�b", MediaTextNormalizer.Normalize("a\uD800b"));
    }
}
