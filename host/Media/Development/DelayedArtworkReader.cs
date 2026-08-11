using NowPlayingOverlay.Host.Artwork;

namespace NowPlayingOverlay.Host.Media.Development;

internal sealed class DelayedArtworkReader(
    ArtworkPayload? payload,
    TimeSpan delay,
    TimeProvider? timeProvider = null) : IArtworkReader
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        await Task.Delay(delay, _timeProvider, cancellationToken);
        return payload;
    }
}
