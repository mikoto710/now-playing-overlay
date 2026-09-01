using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Owns one exact-IPv4 <see cref="HttpListener"/>, its accept loop, and every request accepted by
/// that listener. Stop first prevents new accepts, then waits for the accept loop and all active
/// requests before releasing the listener.
/// </summary>
internal sealed class LoopbackListenerEndpoint
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
        _listener.Close();
        _shutdown.Dispose();
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_acceptLoop is not null)
        {
            await _acceptLoop;
        }

        await Task.WhenAll(_requests.Keys);
        _listener.Close();
        _shutdown.Dispose();
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
                    "The loopback HTTP accept loop failed on port {Port}. Error type {ErrorType}, HRESULT {ErrorHResult}.",
                    Port,
                    error.GetType().Name,
                    error.HResult);
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
}
