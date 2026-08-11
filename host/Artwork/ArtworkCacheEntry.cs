namespace NowPlayingOverlay.Host.Artwork;

internal sealed class ArtworkCacheEntry
{
    private readonly byte[] _bytes;

    internal ArtworkCacheEntry(
        string artworkId,
        string contentType,
        byte[] bytes)
    {
        ArtworkId = artworkId;
        ContentType = contentType;
        _bytes = bytes;
    }

    public string ArtworkId { get; }

    public string ContentType { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public int ByteLength => _bytes.Length;

}
