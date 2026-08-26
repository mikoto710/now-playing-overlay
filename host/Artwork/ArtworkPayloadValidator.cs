namespace NowPlayingOverlay.Host.Artwork;

internal static class ArtworkPayloadValidator
{
    public static bool TryValidate(
        ArtworkPayload payload,
        ArtworkCacheOptions options,
        out string contentType)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);
        var bytes = payload.Bytes;
        contentType = string.Empty;
        return bytes.Length <= options.MaximumItemBytes
            && ArtworkImageInspector.TryInspect(
                bytes.Span,
                out contentType,
                out var width,
                out var height)
            && width <= options.MaximumWidth
            && height <= options.MaximumHeight
            && (long)width * height <= options.MaximumPixels;
    }
}
