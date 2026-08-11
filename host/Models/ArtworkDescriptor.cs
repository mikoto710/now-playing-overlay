namespace NowPlayingOverlay.Host.Models;

internal sealed record ArtworkDescriptor(
    long ArtworkRevision,
    string ArtworkId,
    string ContentType,
    int ByteLength);
