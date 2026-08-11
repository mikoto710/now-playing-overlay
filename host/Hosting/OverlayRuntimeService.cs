using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayRuntimeService(
    NowPlayingCoordinator coordinator,
    HostRuntimeState runtime,
    ILogger<OverlayRuntimeService> logger)
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting the overlay runtime.");
        coordinator.Start();
        runtime.MarkReady();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping the overlay runtime.");
        await coordinator.DisposeAsync();
        logger.LogInformation("The overlay runtime stopped.");
    }
}
