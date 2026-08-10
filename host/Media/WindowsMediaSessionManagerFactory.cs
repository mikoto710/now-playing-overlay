using Windows.Media.Control;

namespace NowPlayingOverlay.Host.Media;

internal sealed class WindowsMediaSessionManagerFactory : IMediaSessionManagerFactory
{
    public async ValueTask<IMediaSessionManager> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsMediaSessionManager(manager);
    }
}
