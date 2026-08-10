using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

using OverlayHostOptions = Configuration.HostOptions;

internal sealed class OverlayRuntimeService(
    NowPlayingCoordinator coordinator,
    FakeSessionSource fakeSource,
    HostRuntimeState runtime,
    OverlayHostOptions options) : IHostedService
{
    private CancellationTokenSource? _scenarioCancellation;
    private Task? _scenario;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        coordinator.Start();
        runtime.MarkReady();
        if (options.RunFakeScenario)
        {
            _scenarioCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _scenario = fakeSource.RunScriptAsync(
                FakeScenario.Create(),
                cancellationToken: _scenarioCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
    }
}
