namespace NowPlayingOverlay.Host.Media.External;

internal static class ExternalIngestConnectionCode
{
    private const string Prefix = "npo1";

    public static string Create(int port, string ingestKey)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ingestKey);
        if (ingestKey.Length != IngestKey.EncodedLength
            || ingestKey.Any(character => character is not (
                >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_')))
        {
            throw new ArgumentException("The ingest key is not canonical base64url.", nameof(ingestKey));
        }

        return $"{Prefix}:{port}:{ingestKey}";
    }
}
