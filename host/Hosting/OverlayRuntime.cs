using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;

namespace NowPlayingOverlay.Host.Hosting;

internal enum OverlayRuntimeState
{
    Created,
    Running,
    Stopped,
    Disposed,
}

/// <summary>
/// Owns the one-shot Created -> Running -> Stopped -> Disposed lifecycle.
/// </summary>
internal sealed class OverlayRuntime : IAsyncDisposable
{
    // Serializes transitions; Stopped instances cannot start again.
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly IOverlayHttpRuntime _httpServer;
    private readonly IOutputRuntime _outputs;
    private readonly INowPlayingRuntime _coordinator;
    private readonly HostRuntimeState _runtimeState;
    private readonly ILogger<OverlayRuntime> _logger;
    private OverlayRuntimeState _state = OverlayRuntimeState.Created;

    public OverlayRuntime(
        HostOptions options,
        HostStatusService statusService,
        IOverlayHttpRuntime httpServer,
        IOutputRuntime outputs,
        INowPlayingRuntime coordinator,
        HostRuntimeState runtimeState,
        ILogger<OverlayRuntime>? logger = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        StatusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _httpServer = httpServer ?? throw new ArgumentNullException(nameof(httpServer));
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _logger = logger ?? NullLogger<OverlayRuntime>.Instance;
    }

    public HostOptions Options { get; }

    public HostStatusService StatusService { get; }

    public int CurrentPort => _httpServer.CurrentPort;

    internal OverlayRuntimeState State => _state;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_state == OverlayRuntimeState.Disposed, this);
            if (_state != OverlayRuntimeState.Created)
            {
                throw new InvalidOperationException(
                    "The overlay runtime can only be started once from the Created state.");
            }

            var serverStarted = false;
            var outputsStarted = false;
            var coordinatorStartAttempted = false;
            try
            {
                await _httpServer.StartAsync(cancellationToken);
                serverStarted = true;
                _outputs.Start();
                outputsStarted = true;
                coordinatorStartAttempted = true;
                _coordinator.Start();
                _runtimeState.MarkReady();
                _state = OverlayRuntimeState.Running;
            }
            catch (Exception startError)
            {
                _state = OverlayRuntimeState.Stopped;
                await CleanupFailedStartAsync(
                    serverStarted,
                    outputsStarted,
                    coordinatorStartAttempted);
                ExceptionDispatchInfo.Capture(startError).Throw();
                throw;
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (_state is OverlayRuntimeState.Stopped or OverlayRuntimeState.Disposed)
            {
                return;
            }

            if (_state == OverlayRuntimeState.Created)
            {
                _state = OverlayRuntimeState.Stopped;
                return;
            }

            // Once shutdown starts, finish best-effort cleanup even if the caller stops waiting.
            await StopRunningComponentsAsync();
            _state = OverlayRuntimeState.Stopped;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transitionGate.WaitAsync();
        try
        {
            if (_state == OverlayRuntimeState.Disposed)
            {
                return;
            }

            Exception? firstError = null;
            if (_state == OverlayRuntimeState.Running)
            {
                try
                {
                    await StopRunningComponentsAsync();
                    _state = OverlayRuntimeState.Stopped;
                }
                catch (Exception error)
                {
                    firstError = error;
                }
            }

            firstError = await DisposeComponentAsync(
                _coordinator,
                "now-playing coordinator",
                firstError);
            firstError = await DisposeComponentAsync(
                _outputs,
                "output workers",
                firstError);
            firstError = await DisposeComponentAsync(
                _httpServer,
                "loopback HTTP server",
                firstError);
            _state = OverlayRuntimeState.Disposed;
            if (firstError is not null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task CleanupFailedStartAsync(
        bool serverStarted,
        bool outputsStarted,
        bool coordinatorStartAttempted)
    {
        // Unwind only attempted components, in reverse dependency order.
        if (coordinatorStartAttempted)
        {
            await DisposeComponentAsync(_coordinator, "partially started coordinator");
        }

        if (outputsStarted)
        {
            await StopComponentAsync(_outputs.StopAsync, "partially started outputs");
        }

        if (serverStarted)
        {
            await StopComponentAsync(
                () => _httpServer.StopAsync(CancellationToken.None),
                "partially started HTTP server");
        }
    }

    private async Task StopRunningComponentsAsync()
    {
        Exception? firstError = null;
        firstError = await StopComponentAsync(
            () => _httpServer.StopAsync(CancellationToken.None),
            "loopback HTTP server",
            firstError);
        firstError = await DisposeComponentAsync(
            _coordinator,
            "now-playing coordinator",
            firstError);
        firstError = await StopComponentAsync(
            _outputs.StopAsync,
            "output workers",
            firstError);

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private async Task<Exception?> StopComponentAsync(
        Func<Task> stop,
        string component,
        Exception? firstError = null)
    {
        try
        {
            await stop();
        }
        catch (Exception error)
        {
            _logger.LogError(
                "Could not stop the {RuntimeComponent}. {Diagnostic}",
                component,
                SanitizedExceptionDiagnostics.Create(error));
            firstError ??= error;
        }

        return firstError;
    }

    private async Task<Exception?> DisposeComponentAsync(
        IAsyncDisposable component,
        string componentName,
        Exception? firstError = null)
    {
        try
        {
            await component.DisposeAsync();
        }
        catch (Exception error)
        {
            _logger.LogError(
                "Could not dispose the {RuntimeComponent}. {Diagnostic}",
                componentName,
                SanitizedExceptionDiagnostics.Create(error));
            firstError ??= error;
        }

        return firstError;
    }
}
