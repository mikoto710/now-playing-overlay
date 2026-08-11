namespace NowPlayingOverlay.Host.Media.Windows;

internal interface IMediaSessionManagerFactory
{
    ValueTask<IMediaSessionManager> CreateAsync(CancellationToken cancellationToken);
}
