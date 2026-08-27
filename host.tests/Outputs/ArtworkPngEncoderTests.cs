using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Tests.Outputs;

public sealed class ArtworkPngEncoderTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAQTGF2YzYyLjExLjEwMAD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/xABLAAEBAAAAAAAAAAAAAAAAAAAABgEBAAAAAAAAAAAAAAAAAAAABhABAAAAAAAAAAAAAAAAAAAAABEBAAAAAAAAAAAAAAAAAAAAAP/AABEIAAIAAgMBIgACEQADEQD/2gAMAwEAAhEDEQA/AIsAUCX/2Q==");
    private static readonly byte[] WebP = Convert.FromBase64String(
        "UklGRjgAAABXRUJQVlA4ICwAAACQAQCdASoCAAIAAgA0JaACdLoAA5gA/vmTb/+QH/+QH/+QH/8gP+IXeyAwAA==");

    [Theory]
    [MemberData(nameof(EncodedArtwork))]
    public async Task ProducesRealPngBytesForEveryAcceptedInput(
        byte[] input,
        string contentType)
    {
        var entry = new ArtworkCacheEntry("artwork-id", contentType, input);
        var encoder = new ArtworkPngEncoder();

        var encoded = await encoder.EncodeAsync(entry, CancellationToken.None);

        Assert.True(encoded.Span.StartsWith(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }));
    }

    public static TheoryData<byte[], string> EncodedArtwork => new()
    {
        { Png, "image/png" },
        { Jpeg, "image/jpeg" },
        { WebP, "image/webp" },
    };
}
