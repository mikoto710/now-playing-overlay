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

        if (source is not ISessionSourceStatus status)
        {
            return new HostStatus("Source Status Unavailable", IsFaulted: false);
        }

        var state = status.GetState();
        if (state.Status == SourceStatus.Faulted)
        {
            return new HostStatus("Source Faulted - Open Logs For Details", IsFaulted: true);
        }

        if (state.Status == SourceStatus.Unconfigured)
        {
            return new HostStatus("Source Not Configured", IsFaulted: false);
        }

        var provider = state.ActiveSource?.Key.Provider.ToDisplayName() ?? "Source";
        if (state.Status == SourceStatus.Starting)
        {
            return new HostStatus($"{provider}: Starting", IsFaulted: false);
        }

        if (state.Status == SourceStatus.Unavailable)
        {
            var detail = state.Reason switch
            {
                SourceStatusReason.Missing => "Selected Player Not Available",
                SourceStatusReason.Ambiguous => "Selected Player Is Ambiguous",
                SourceStatusReason.PlatformUnavailable => "Sessions Unavailable",
                _ => "Unavailable",
            };
            return new HostStatus($"{provider}: {detail}", IsFaulted: false);
        }

        return new HostStatus($"{provider}: {store.Current.Playback}", IsFaulted: false);
    }
}
