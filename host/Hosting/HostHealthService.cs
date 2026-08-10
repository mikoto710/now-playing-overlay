using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class HostHealthService(
    HostRuntimeState runtime,
    NowPlayingStore store,
    NowPlayingCoordinator coordinator,
    ISessionSource source,
    TimeProvider timeProvider)
{
    public (HealthDto Body, int StatusCode) GetHealth()
    {
        var snapshot = store.Current;
        var faulted = coordinator.LastError is not null;
        var status = faulted ? "faulted" : runtime.IsReady ? "ready" : "starting";
        var sourceStatus = source as ISessionSourceStatus;
        // Health exposes operational state without leaking media or exception details.
        var body = new HealthDto
        {
            HostStatus = status,
            SessionManagerAvailable = sourceStatus?.IsAvailable ?? false,
            SpotifySessionBound = snapshot.SourceAppUserModelId.Length > 0,
            ServerInstanceId = snapshot.ServerInstanceId,
            SnapshotRevision = snapshot.SnapshotRevision,
            UptimeSeconds = Math.Max(
                0,
                (long)(timeProvider.GetUtcNow() - runtime.StartedAt).TotalSeconds),
        };

        return (body, faulted ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
    }
}
