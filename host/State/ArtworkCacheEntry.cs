namespace NowPlayingOverlay.Host.State;

internal sealed class ArtworkCacheEntry
{
    private readonly byte[] _bytes;

    internal ArtworkCacheEntry(
        string artworkId,
        string contentType,
        byte[] bytes,
        int width,
        int height)
    {
        ArtworkId = artworkId;
        ContentType = contentType;
        _bytes = bytes;
        Width = width;
        Height = height;
    }

    public string ArtworkId { get; }

    public string ContentType { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public int ByteLength => _bytes.Length;

    public int Width { get; }

    public int Height { get; }
}
