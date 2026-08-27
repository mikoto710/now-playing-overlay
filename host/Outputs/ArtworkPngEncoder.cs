using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using NowPlayingOverlay.Host.Artwork;

namespace NowPlayingOverlay.Host.Outputs;

internal sealed class ArtworkPngEncoder
{
    public async Task<ReadOnlyMemory<byte>> EncodeAsync(
        ArtworkCacheEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.ContentType == "image/png")
        {
            return entry.Bytes;
        }

        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(entry.Bytes.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        cancellationToken.ThrowIfCancellationRequested();

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        cancellationToken.ThrowIfCancellationRequested();

        output.Seek(0);
        var bytes = new byte[checked((int)output.Size)];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)bytes.Length);
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded != bytes.Length)
        {
            throw new EndOfStreamException(
                $"Expected {bytes.Length} encoded PNG bytes but read {loaded}.");
        }

        reader.ReadBytes(bytes);
        return bytes;
    }
}
