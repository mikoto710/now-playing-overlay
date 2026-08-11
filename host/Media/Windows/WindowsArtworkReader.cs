using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Windows;

internal sealed class WindowsArtworkReader : IArtworkReader
{
    private const int MaximumWidth = 4096;
    private const int MaximumHeight = 4096;
    private const long MaximumPixels = 16_777_216;
    private readonly Func<ValueTask<IRandomAccessStream>> _openStream;

    public WindowsArtworkReader(IRandomAccessStreamReference thumbnail)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        _openStream = async () => await thumbnail.OpenReadAsync();
    }

    internal WindowsArtworkReader(Func<ValueTask<IRandomAccessStream>> openStream)
    {
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
    }

    public async ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await _openStream();
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Size is 0 or > ArtworkCacheOptions.DefaultMaximumItemBytes)
        {
            return null;
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var contentType = GetContentType(decoder.DecoderInformation.CodecId);
        if (contentType is null
            || decoder.PixelWidth is 0 or > MaximumWidth
            || decoder.PixelHeight is 0 or > MaximumHeight
            || (long)decoder.PixelWidth * decoder.PixelHeight > MaximumPixels)
        {
            return null;
        }

        // Force pixel decoding before publishing the original encoded bytes.
        var pixels = await decoder.GetPixelDataAsync();
        _ = pixels.DetachPixelData();
        cancellationToken.ThrowIfCancellationRequested();

        stream.Seek(0);
        var bytes = new byte[checked((int)stream.Size)];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)bytes.Length);
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded != bytes.Length)
        {
            throw new EndOfStreamException(
                $"Expected {bytes.Length} thumbnail bytes but read {loaded}.");
        }

        reader.ReadBytes(bytes);
        return ArtworkPayload.Create(bytes);
    }

    private static string? GetContentType(Guid codecId)
    {
        if (codecId == BitmapDecoder.PngDecoderId)
        {
            return "image/png";
        }

        if (codecId == BitmapDecoder.JpegDecoderId)
        {
            return "image/jpeg";
        }

        if (codecId == BitmapDecoder.WebpDecoderId)
        {
            return "image/webp";
        }

        return null;
    }
}
