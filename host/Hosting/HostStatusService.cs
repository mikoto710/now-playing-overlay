using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class HostStatusService(
    HostRuntimeState runtime,
    NowPlayingCoordinator coordinator,
    ISessionSource source,
    NowPlayingStore store)
{
    public HostStatus GetCurrent()
    {
        if (coordinator.LastError is not null)
        {
            return new HostStatus("Host Faulted - Open Logs For Details", IsFaulted: true);
        }

        if (!runtime.IsReady)
        {
            return new HostStatus("Host Starting", IsFaulted: false);
        }

        if (source is not ISessionSourceStatus status || !status.IsAvailable)
        {
            return new HostStatus("Windows Media Sessions Unavailable", IsFaulted: false);
        }

        var snapshot = store.Current;
        if (snapshot.SourceAppUserModelId.Length == 0)
        {
            return new HostStatus("Waiting for Spotify", IsFaulted: false);
        }

        return new HostStatus($"Spotify: {snapshot.Playback}", IsFaulted: false);
    }
}
