namespace NowPlayingOverlay.Host.Artwork;

internal sealed class ArtworkPayload
{
    private readonly byte[] _bytes;

    private ArtworkPayload(byte[] bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static ArtworkPayload Create(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("Artwork payload must not be empty.", nameof(bytes));
        }

        return new ArtworkPayload(bytes.ToArray());
    }
}
