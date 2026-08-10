using System.Buffers.Binary;

namespace NowPlayingOverlay.Host.State;

internal static class ArtworkImageInspector
{
    public static bool TryInspect(
        ReadOnlySpan<byte> bytes,
        out string contentType,
        out int width,
        out int height)
    {
        if (TryInspectPng(bytes, out width, out height))
        {
            contentType = "image/png";
            return true;
        }

        if (TryInspectJpeg(bytes, out width, out height))
        {
            contentType = "image/jpeg";
            return true;
        }

        if (TryInspectWebP(bytes, out width, out height))
        {
            contentType = "image/webp";
            return true;
        }

        contentType = string.Empty;
        width = 0;
        height = 0;
        return false;
    }

    private static bool TryInspectPng(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        width = 0;
        height = 0;
        if (bytes.Length < 45 || !bytes[..8].SequenceEqual(signature))
        {
            return false;
        }

        var offset = 8;
        var sawHeader = false;
        var sawImageData = false;
        while (offset + 12 <= bytes.Length)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..(offset + 4)]);
            if (chunkLength > int.MaxValue || offset + 12L + chunkLength > bytes.Length)
            {
                return false;
            }

            var type = bytes[(offset + 4)..(offset + 8)];
            if (!sawHeader)
            {
                if (chunkLength != 13 || !type.SequenceEqual("IHDR"u8))
                {
                    return false;
                }

                var parsedWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8)..(offset + 12)]);
                var parsedHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 12)..(offset + 16)]);
                if (parsedWidth is 0 or > int.MaxValue || parsedHeight is 0 or > int.MaxValue)
                {
                    return false;
                }

                width = (int)parsedWidth;
                height = (int)parsedHeight;
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                sawImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                return chunkLength == 0 && sawImageData && offset + 12 == bytes.Length;
            }

            offset += checked((int)chunkLength + 12);
        }

        return false;
    }

    private static bool TryInspectJpeg(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 11
            || bytes[0] != 0xff
            || bytes[1] != 0xd8
            || bytes[^2] != 0xff
            || bytes[^1] != 0xd9)
        {
            return false;
        }

        var offset = 2;
        var parsedWidth = 0;
        var parsedHeight = 0;
        while (offset + 3 < bytes.Length - 2)
        {
            if (bytes[offset++] != 0xff)
            {
                return false;
            }

            while (offset < bytes.Length && bytes[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            var marker = bytes[offset++];
            if (marker is 0x01 or >= 0xd0 and <= 0xd9)
            {
                continue;
            }

            if (offset + 2 > bytes.Length)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..(offset + 2)]);
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                parsedHeight = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..(offset + 5)]);
                parsedWidth = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..(offset + 7)]);
                if (parsedWidth == 0 || parsedHeight == 0)
                {
                    return false;
                }
            }

            if (marker == 0xda)
            {
                width = parsedWidth;
                height = parsedHeight;
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker)
    {
        return marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7
            or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;
    }

    private static bool TryInspectWebP(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 30
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) != bytes.Length - 8)
        {
            return false;
        }

        var chunk = bytes[12..16];
        var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        if (chunk.SequenceEqual("VP8X"u8) && chunkLength >= 10)
        {
            width = 1 + ReadUInt24LittleEndian(bytes[24..27]);
            height = 1 + ReadUInt24LittleEndian(bytes[27..30]);
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8) && chunkLength >= 5 && bytes[20] == 0x2f)
        {
            width = 1 + bytes[21] + ((bytes[22] & 0x3f) << 8);
            height = 1 + (bytes[22] >> 6) + (bytes[23] << 2) + ((bytes[24] & 0x0f) << 10);
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8)
            && chunkLength >= 10
            && bytes[23] == 0x9d
            && bytes[24] == 0x01
            && bytes[25] == 0x2a)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..28]) & 0x3fff;
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..30]) & 0x3fff;
            return width > 0 && height > 0;
        }

        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes)
    {
        return bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
    }
}
