namespace NowPlayingOverlay.Host.Models;

internal sealed record ArtworkDescriptor
{
    public const int MaximumByteLength = 5 * 1024 * 1024;

    private static readonly HashSet<string> SupportedContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    private ArtworkDescriptor(
        long artworkRevision,
        string artworkId,
        string contentType,
        int byteLength)
    {
        ArtworkRevision = artworkRevision;
        ArtworkId = artworkId;
        ContentType = contentType;
        ByteLength = byteLength;
    }

    public long ArtworkRevision { get; }

    public string ArtworkId { get; }

    public string ContentType { get; }

    public int ByteLength { get; }

    public static ArtworkDescriptor Create(
        long artworkRevision,
        string artworkId,
        string contentType,
        int byteLength)
    {
        if (artworkRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(artworkRevision),
                artworkRevision,
                "Artwork revision must be positive.");
        }

        if (!IsLowercaseSha256(artworkId))
        {
            throw new ArgumentException(
                "Artwork ID must be a lowercase SHA-256 hexadecimal value.",
                nameof(artworkId));
        }

        if (!SupportedContentTypes.Contains(contentType))
        {
            throw new ArgumentException("Artwork content type is not supported.", nameof(contentType));
        }

        if (byteLength is <= 0 or > MaximumByteLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                byteLength,
                $"Artwork byte length must be between 1 and {MaximumByteLength}.");
        }

        return new ArtworkDescriptor(
            artworkRevision,
            artworkId,
            contentType.ToLowerInvariant(),
            byteLength);
    }

    private static bool IsLowercaseSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
