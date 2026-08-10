namespace NowPlayingOverlay.Host.Media;

internal sealed class ArtworkPayload
{
    private readonly byte[] _bytes;

    private ArtworkPayload(byte[] bytes, string? declaredContentType)
    {
        _bytes = bytes;
        DeclaredContentType = declaredContentType;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string? DeclaredContentType { get; }

    public static ArtworkPayload Create(ReadOnlySpan<byte> bytes, string? declaredContentType = null)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("Artwork payload must not be empty.", nameof(bytes));
        }

        var contentType = string.IsNullOrWhiteSpace(declaredContentType)
            ? null
            : declaredContentType.Trim();
        return new ArtworkPayload(bytes.ToArray(), contentType);
    }
}
