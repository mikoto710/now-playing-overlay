using System.Net;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Authorization;

public sealed class SpotifyLoopbackCallbackListenerTests
{
    [Fact]
    public async Task AcceptsOneMatchingLoopbackCallback()
    {
        using var listener = new SpotifyLoopbackCallbackListener();
        using var client = CreateClient();
        var callback = listener.WaitForAuthorizationCodeAsync(
            "expected-state",
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        var response = await client.GetAsync(
            new Uri(listener.RedirectUri, "?code=authorization-code&state=expected-state"));
        var code = await callback;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authorization-code", code);
    }

    [Fact]
    public async Task RejectsAMismatchedStateBeforeReturningTheCode()
    {
        using var listener = new SpotifyLoopbackCallbackListener();
        using var client = CreateClient();
        var callback = listener.WaitForAuthorizationCodeAsync(
            "expected-state",
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        var response = await client.GetAsync(
            new Uri(listener.RedirectUri, "?code=authorization-code&state=wrong-state"));
        var error = await Assert.ThrowsAsync<SpotifyAuthorizationException>(() => callback);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("state", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }
}
