using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Artwork;

internal sealed record ArtworkCacheOptions
{
    public int MaximumItemBytes { get; init; } = ArtworkDescriptor.MaximumByteLength;

    public int MaximumWidth { get; init; } = 4096;

    public int MaximumHeight { get; init; } = 4096;

    public long MaximumPixels { get; init; } = 16_777_216;

    public int MaximumEntries { get; init; } = 4;

    public int MaximumTotalBytes { get; init; } = 16 * 1024 * 1024;

    public void Validate()
    {
        if (MaximumItemBytes is <= 0 or > ArtworkDescriptor.MaximumByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumItemBytes));
        }

        if (MaximumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumWidth));
        }

        if (MaximumHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumHeight));
        }

        if (MaximumPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPixels));
        }

        if (MaximumEntries < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumEntries),
                "Artwork cache must be able to retain current and previous entries.");
        }

        if (MaximumTotalBytes < MaximumItemBytes * 2L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumTotalBytes),
                "Artwork cache must fit at least two maximum-size entries.");
        }
    }
}
