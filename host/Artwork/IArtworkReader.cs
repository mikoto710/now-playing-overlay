namespace NowPlayingOverlay.Host.Artwork;

internal interface IArtworkReader
{
    // Platform readers decode first, then return the original validated bytes.
    ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken);
}
