using System.Diagnostics;
using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NowPlayingOverlay.SessionProbe;

internal sealed class ThumbnailInspector
{
    private const ulong MaximumDiagnosticThumbnailBytes = 32 * 1024 * 1024;

    private readonly ProbeLogSink _sink;

    public ThumbnailInspector(ProbeLogSink sink)
    {
        _sink = sink;
    }

    public async Task InspectAsync(
        string source,
        long readId,
        IRandomAccessStreamReference thumbnail)
    {
        await _sink.WriteAsync("thumbnail-read-started", source, new { readId });
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var reportedSize = stream.Size;
            // Bound allocation before converting the WinRT size to an array length.
            if (reportedSize > MaximumDiagnosticThumbnailBytes)
            {
                await _sink.WriteAsync(
                    "thumbnail-rejected",
                    source,
                    new
                    {
                        readId,
                        reason = "reported-size-exceeds-diagnostic-limit",
                        reportedSize,
                        limit = MaximumDiagnosticThumbnailBytes,
                    });
                return;
            }

            string? decoderFormat = null;
            uint? pixelWidth = null;
            uint? pixelHeight = null;
            try
            {
                // Decode for dimensions and format instead of trusting ContentType.
                stream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                decoderFormat = decoder.DecoderInformation.FriendlyName;
                pixelWidth = decoder.PixelWidth;
                pixelHeight = decoder.PixelHeight;
            }
            catch (Exception exception)
            {
                await WriteErrorAsync("thumbnail-decode-failed", source, exception, new { readId });
            }

            // Rewind after decoding so the hash covers the full payload.
            stream.Seek(0);
            var bytes = await ReadBytesAsync(stream);
            var detectedContentType = ImageSignatureDetector.DetectContentType(bytes);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await _sink.WriteAsync(
                "thumbnail-read-completed",
                source,
                new
                {
                    readId,
                    elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    streamContentType = stream.ContentType,
                    detectedContentType,
                    byteLength = bytes.Length,
                    reportedSize,
                    pixelWidth,
                    pixelHeight,
                    decoderFormat,
                    sha256 = hash,
                });
        }
        catch (Exception exception)
        {
            await WriteErrorAsync("thumbnail-read-failed", source, exception, new { readId });
        }
    }

    private static async Task<byte[]> ReadBytesAsync(IRandomAccessStreamWithContentType stream)
    {
        var size = checked((int)stream.Size);
        var bytes = new byte[size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)size);
        if (loaded != size)
        {
            throw new EndOfStreamException($"Expected {size} thumbnail bytes but read {loaded}.");
        }

        reader.ReadBytes(bytes);
        return bytes;
    }

    private Task WriteErrorAsync(
        string eventName,
        string source,
        Exception exception,
        object? context = null)
    {
        return _sink.WriteAsync(
            eventName,
            source,
            new
            {
                context,
                exceptionType = exception.GetType().FullName,
                exception.HResult,
                exception.Message,
            });
    }
}
