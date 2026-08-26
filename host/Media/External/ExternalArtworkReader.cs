using NowPlayingOverlay.Host.Artwork;

namespace NowPlayingOverlay.Host.Media.External;

internal sealed class ExternalArtworkReader(ArtworkPayload payload) : IArtworkReader
{
    private readonly ArtworkPayload _payload = payload ?? throw new ArgumentNullException(nameof(payload));

    public ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ArtworkPayload?>(_payload);
    }
}
