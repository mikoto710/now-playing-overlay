namespace NowPlayingOverlay.Host.Media;

internal interface IMediaSessionManagerFactory
{
    ValueTask<IMediaSessionManager> CreateAsync(CancellationToken cancellationToken);
}
