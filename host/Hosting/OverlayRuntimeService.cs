using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Development;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

using OverlayHostOptions = Configuration.HostOptions;

internal sealed class OverlayRuntimeService(
    NowPlayingCoordinator coordinator,
    ISessionSource sessionSource,
    HostRuntimeState runtime,
    OverlayHostOptions options,
    ILogger<OverlayRuntimeService> logger)
{
    private CancellationTokenSource? _scenarioCancellation;
    private Task? _scenario;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting the overlay runtime on port {Port}.", options.Port);
        coordinator.Start();
        runtime.MarkReady();
        if (options.RunFakeScenario)
        {
            var fakeSource = sessionSource as FakeSessionSource
                ?? throw new InvalidOperationException("The fake scenario requires the fake session source.");
            _scenarioCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _scenario = fakeSource.RunScriptAsync(
                FakeScenario.Create(),
                cancellationToken: _scenarioCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping the overlay runtime.");
        if (_scenarioCancellation is not null)
        {
            await _scenarioCancellation.CancelAsync();
        }

        if (_scenario is not null)
        {
            try
            {
                await _scenario.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await coordinator.DisposeAsync();
        _scenarioCancellation?.Dispose();
        logger.LogInformation("The overlay runtime stopped.");
    }
}
