namespace NowPlayingOverlay.Host.Media;

internal interface IArtworkReader
{
    // Platform readers decode first, then return the original validated bytes.
    ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken);
}
