using System.Collections.Concurrent;
using System.Net;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;

namespace NowPlayingOverlay.Host.Hosting;

internal interface ILoopbackListenerEndpoint
{
    int Port { get; }

    void Start();

    void CloseAfterFailedStart();

    Task StopAsync();
}

/// <summary>Owns one listener, its accept loop, and accepted requests.</summary>
internal sealed class LoopbackListenerEndpoint : ILoopbackListenerEndpoint
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerContext, CancellationToken, Task> _handleContext;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Task, byte> _requests = new();
    private Task? _acceptLoop;
    private int _stopped;

    public LoopbackListenerEndpoint(
        int port,
        HostOptions options,
        Func<HttpListenerContext, CancellationToken, Task> handleContext,
        ILogger logger)
    {
        Port = port;
        _handleContext = handleContext ?? throw new ArgumentNullException(nameof(handleContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
        _listener.IgnoreWriteExceptions = false;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.TimeoutManager.HeaderWait = options.RequestHeadersTimeout;
        _listener.TimeoutManager.IdleConnection = options.KeepAliveTimeout;
    }

    public int Port { get; }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public void CloseAfterFailedStart()
    {
        Interlocked.Exchange(ref _stopped, 1);
        Exception? firstError = null;
        try
        {
            _listener.Close();
        }
        catch (Exception error)
        {
            firstError = error;
        }

        try
        {
            _shutdown.Dispose();
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        Exception? firstError = null;
        try
        {
            _shutdown.Cancel();
        }
        catch (Exception error)
        {
            firstError = error;
        }

        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        var acceptedBeforeStop = _requests.Keys.ToArray();
        if (_acceptLoop is not null)
        {
            firstError = await CaptureFailureAsync(_acceptLoop, firstError);
        }

        var requests = acceptedBeforeStop
            .Concat(_requests.Keys)
            .Distinct()
            .ToArray();
        firstError = await CaptureFailureAsync(Task.WhenAll(requests), firstError);
        try
        {
            _listener.Close();
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        try
        {
            _shutdown.Dispose();
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_shutdown.Token);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                _logger.LogError(
                    "The loopback HTTP accept loop failed on port {Port}. {Diagnostic}",
                    Port,
                    SanitizedExceptionDiagnostics.Create(error));
                break;
            }

            var request = _handleContext(context, _shutdown.Token);
            _requests.TryAdd(request, 0);
            _ = request.ContinueWith(
                completed => _requests.TryRemove(completed, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task<Exception?> CaptureFailureAsync(
        Task task,
        Exception? firstError)
    {
        try
        {
            await task;
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        return firstError;
    }
}
