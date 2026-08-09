namespace NowPlayingOverlay.SessionProbe;

internal static class ImageSignatureDetector
{
    public static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.StartsWith(new byte[] { 0x42, 0x4D }))
        {
            return "image/bmp";
        }

        return null;
    }
}
