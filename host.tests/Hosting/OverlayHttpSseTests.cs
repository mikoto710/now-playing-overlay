using System.Net;
using System.Net.Http.Headers;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed partial class OverlayHttpTests
{
    [Fact]
    public async Task SseImmediatelySendsFullStateAndThenOnlyNewRevisions()
    {
        await using var host = await TestOverlayHost.StartAsync(heartbeatMilliseconds: 50);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v3/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "old-instance:999");
        using var response = await host.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var initial = await ReadSseEventAsync(reader);
        host.Source.Publish(Playing("SSE track"));
        var changed = await ReadSseEventAsync(reader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("state", initial.Event);
        Assert.EndsWith(":0", initial.Id, StringComparison.Ordinal);
        Assert.Contains("\"protocolVersion\":3", initial.Data, StringComparison.Ordinal);
        Assert.Contains("\"snapshotRevision\":0", initial.Data, StringComparison.Ordinal);
        Assert.Contains("\"timeline\":null", initial.Data, StringComparison.Ordinal);
        Assert.Equal("state", changed.Event);
        Assert.EndsWith(":1", changed.Id, StringComparison.Ordinal);
        Assert.Contains("SSE track", changed.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SseConnectionLimitRejectsAdditionalClient()
    {
        await using var host = await TestOverlayHost.StartAsync(maximumSseConnections: 1);
        using var first = await host.Client.GetAsync(
            "/api/v3/events",
            HttpCompletionOption.ResponseHeadersRead);
        using var second = await host.Client.GetAsync(
            "/api/v3/events",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("1", second.Headers.RetryAfter!.ToString());
    }

    [Fact]
    public async Task TotalActiveRequestLimitIsSharedWithSseConnections()
    {
        await using var host = await TestOverlayHost.StartAsync(
            maximumSseConnections: 1,
            maximumConcurrentConnections: 1);
        using var stream = await host.Client.GetAsync(
            "/api/v3/events",
            HttpCompletionOption.ResponseHeadersRead);

        using var rejected = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Equal("1", rejected.Headers.RetryAfter!.ToString());
    }
}
