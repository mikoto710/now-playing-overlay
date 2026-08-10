using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class TrayStatusService(
    HostRuntimeState runtime,
    NowPlayingCoordinator coordinator,
    ISessionSource source,
    NowPlayingStore store)
{
    public TrayStatus GetCurrent()
    {
        if (coordinator.LastError is not null)
        {
            return new TrayStatus("Host faulted - open logs for details", IsFaulted: true);
        }

        if (!runtime.IsReady)
        {
            return new TrayStatus("Host starting", IsFaulted: false);
        }

        if (source is not ISessionSourceStatus status || !status.IsAvailable)
        {
            return new TrayStatus("Windows media sessions unavailable", IsFaulted: false);
        }

        var snapshot = store.Current;
        if (snapshot.SourceAppUserModelId.Length == 0)
        {
            return new TrayStatus("Waiting for Spotify", IsFaulted: false);
        }

        return new TrayStatus($"Spotify: {snapshot.Playback.ToString().ToLowerInvariant()}", IsFaulted: false);
    }
}
