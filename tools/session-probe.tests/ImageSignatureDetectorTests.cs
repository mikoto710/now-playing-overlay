namespace NowPlayingOverlay.SessionProbe.Tests;

public sealed class ImageSignatureDetectorTests
{
    public static TheoryData<byte[], string?> Signatures =>
        new()
        {
            { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png" },
            { [0xFF, 0xD8, 0xFF, 0xE0], "image/jpeg" },
            { "GIF89a"u8.ToArray(), "image/gif" },
            { "RIFF0000WEBP"u8.ToArray(), "image/webp" },
            { [0x42, 0x4D, 0x00], "image/bmp" },
            { [0x00, 0x01, 0x02], null },
        };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void DetectContentTypeRecognizesSupportedSignatures(byte[] bytes, string? expected)
    {
        Assert.Equal(expected, ImageSignatureDetector.DetectContentType(bytes));
    }
}
