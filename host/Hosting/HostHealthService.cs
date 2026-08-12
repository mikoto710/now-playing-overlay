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
        var sourceState = source is ISessionSourceStatus sourceStatus
            ? sourceStatus.GetState()
            : SourceManagerState.Unconfigured;
        var faulted = coordinator.LastError is not null || sourceState.Status == SourceStatus.Faulted;
        var status = faulted ? "faulted" : runtime.IsReady ? "ready" : "starting";
        // Health exposes operational state without leaking media or exception details.
        var body = new HealthDto
        {
            HostStatus = status,
            ActiveSourceProvider = sourceState.ActiveSource?.Key.Provider.ToProtocolValue(),
            SourceStatus = sourceState.Status.ToProtocolValue(),
            ServerInstanceId = snapshot.ServerInstanceId,
            SnapshotRevision = snapshot.SnapshotRevision,
            UptimeSeconds = Math.Max(
                0,
                (long)(timeProvider.GetUtcNow() - runtime.StartedAt).TotalSeconds),
        };

        return (body, faulted ? 503 : 200);
    }
}
