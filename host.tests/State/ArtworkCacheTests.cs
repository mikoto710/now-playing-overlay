using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Tests.State;

public sealed class ArtworkCacheTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void TryAddUsesSignatureContentTypeAndContentAddress()
    {
        var cache = CreateCache();
        var payload = ArtworkPayload.Create(OnePixelPng, "application/octet-stream");

        Assert.True(cache.TryAdd(payload, out var entry, out var added));

        Assert.True(added);
        Assert.NotNull(entry);
        Assert.Equal("image/png", entry.ContentType);
        Assert.Equal(1, entry.Width);
        Assert.Equal(1, entry.Height);
        Assert.Equal(64, entry.ArtworkId.Length);
        Assert.Equal(entry.ArtworkId.ToLowerInvariant(), entry.ArtworkId);
        Assert.True(cache.TryGet(entry.ArtworkId, out var cached));
        Assert.Same(entry, cached);
    }

    [Fact]
    public void TryAddDeduplicatesIdenticalContent()
    {
        var cache = CreateCache();
        var payload = ArtworkPayload.Create(OnePixelPng);

        Assert.True(cache.TryAdd(payload, out var first, out var firstAdded));
        Assert.True(cache.TryAdd(payload, out var second, out var secondAdded));

        Assert.True(firstAdded);
        Assert.False(secondAdded);
        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    [MemberData(nameof(SupportedImages))]
    public void TryAddAcceptsEverySupportedImageFormat(byte[] bytes, string expectedContentType)
    {
        var cache = CreateCache();

        Assert.True(cache.TryAdd(ArtworkPayload.Create(bytes), out var entry, out _));
        Assert.Equal(expectedContentType, entry!.ContentType);
        Assert.Equal(1, entry.Width);
        Assert.Equal(1, entry.Height);
    }

    [Fact]
    public void TryAddRejectsInvalidSignatureAndDimensionLimits()
    {
        var cache = CreateCache(maximumWidth: 1);
        var oversizedDimensions = OnePixelPng.ToArray();
        oversizedDimensions[19] = 2;

        Assert.False(
            cache.TryAdd(ArtworkPayload.Create([1, 2, 3]), out _, out _));
        Assert.False(
            cache.TryAdd(ArtworkPayload.Create(oversizedDimensions), out _, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void TryAddHonorsItemAndTotalByteLimits()
    {
        var cache = CreateCache(maximumItemBytes: OnePixelPng.Length - 1);

        Assert.False(
            cache.TryAdd(ArtworkPayload.Create(OnePixelPng), out _, out _));
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public void TotalByteLimitEvictsLeastRecentEntry()
    {
        var cache = CreateCache(
            maximumEntries: 4,
            maximumItemBytes: OnePixelPng.Length,
            maximumTotalBytes: OnePixelPng.Length * 2);
        cache.TryAdd(CreateDistinctPng(1), out var first, out _);
        cache.TryAdd(CreateDistinctPng(2), out var second, out _);

        Assert.True(cache.TryAdd(CreateDistinctPng(3), out var third, out _));

        Assert.False(cache.TryGet(first!.ArtworkId, out _));
        Assert.True(cache.TryGet(second!.ArtworkId, out _));
        Assert.True(cache.TryGet(third!.ArtworkId, out _));
        Assert.True(cache.TotalBytes <= OnePixelPng.Length * 2);
    }

    [Fact]
    public void EvictionKeepsProtectedCurrentAndRemovesLeastRecentUnprotectedEntry()
    {
        var cache = CreateCache(maximumEntries: 2);
        var firstPayload = CreateDistinctPng(1);
        var secondPayload = CreateDistinctPng(2);
        var thirdPayload = CreateDistinctPng(3);
        cache.TryAdd(firstPayload, out var first, out _);
        cache.TryAdd(secondPayload, out var second, out _);
        cache.SetProtectedIds(first!.ArtworkId);

        Assert.True(cache.TryAdd(thirdPayload, out var third, out _));

        Assert.True(cache.TryGet(first.ArtworkId, out _));
        Assert.False(cache.TryGet(second!.ArtworkId, out _));
        Assert.True(cache.TryGet(third!.ArtworkId, out _));
    }

    [Fact]
    public void TryAddDoesNotEvictProtectedCurrentAndPreviousEntries()
    {
        var cache = CreateCache(maximumEntries: 2);
        cache.TryAdd(CreateDistinctPng(1), out var first, out _);
        cache.TryAdd(CreateDistinctPng(2), out var second, out _);
        cache.SetProtectedIds(first!.ArtworkId, second!.ArtworkId);

        Assert.False(cache.TryAdd(CreateDistinctPng(3), out _, out _));
        Assert.Equal(2, cache.Count);
    }

    private static ArtworkCache CreateCache(
        int maximumEntries = 4,
        int maximumItemBytes = 1024,
        int maximumWidth = 4096,
        int? maximumTotalBytes = null)
    {
        return new ArtworkCache(
            new ArtworkCacheOptions
            {
                MaximumEntries = maximumEntries,
                MaximumItemBytes = maximumItemBytes,
                MaximumTotalBytes = maximumTotalBytes ?? maximumItemBytes * 4,
                MaximumWidth = maximumWidth,
            });
    }

    private static ArtworkPayload CreateDistinctPng(byte value)
    {
        var bytes = OnePixelPng.ToArray();
        bytes[^13] = value;
        return ArtworkPayload.Create(bytes);
    }

    public static TheoryData<byte[], string> SupportedImages =>
        new()
        {
            { OnePixelPng, "image/png" },
            { CreateMinimalJpeg(), "image/jpeg" },
            { CreateMinimalWebP(), "image/webp" },
        };

    private static byte[] CreateMinimalJpeg()
    {
        return
        [
            0xff, 0xd8,
            0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
            0xff, 0xda, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
            0x00,
            0xff, 0xd9,
        ];
    }

    private static byte[] CreateMinimalWebP()
    {
        return
        [
            0x52, 0x49, 0x46, 0x46, 0x16, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50,
            0x56, 0x50, 0x38, 0x58, 0x0a, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
        ];
    }
}
