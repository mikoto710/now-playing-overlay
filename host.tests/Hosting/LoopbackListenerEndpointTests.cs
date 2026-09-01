using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class LoopbackListenerEndpointTests
{
    [Fact]
    public async Task ListenerRequestFaultStillClosesListener()
    {
        var port = ReservePort();
        var requestEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new LoopbackListenerEndpoint(
            port,
            new HostOptions(),
            async (_, _) =>
            {
                requestEntered.TrySetResult();
                await releaseRequest.Task;
                throw new IOException("request failed");
            },
            NullLogger.Instance);
        endpoint.Start();
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        var request = client.GetAsync("/health");
        await requestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stop = endpoint.StopAsync();
        releaseRequest.TrySetResult();

        await Assert.ThrowsAsync<IOException>(() => stop);
        try
        {
            using var response = await request;
        }
        catch (HttpRequestException)
        {
        }

        using var replacement = new TcpListener(IPAddress.Loopback, port);
        replacement.Start();
        await endpoint.StopAsync();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
