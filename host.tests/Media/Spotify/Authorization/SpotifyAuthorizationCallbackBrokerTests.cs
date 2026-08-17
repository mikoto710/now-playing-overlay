using System.Net;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Authorization;

public sealed class SpotifyAuthorizationCallbackBrokerTests
{
    [Fact]
    public async Task AcceptsOneMatchingCallbackOnTheConfiguredRoute()
    {
        var broker = new SpotifyAuthorizationCallbackBroker();
        using var registration = broker.Begin("expected-state");

        var handled = broker.TryComplete(
            new Uri(
                "http://127.0.0.1:13130/oauth/spotify/callback?code=authorization-code&state=expected-state"),
            out var response);
        var code = await registration.WaitForAuthorizationCodeAsync(
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authorization-code", code);
        Assert.False(broker.HasPendingAuthorization);
    }

    [Fact]
    public async Task RejectsAMismatchedStateBeforeReturningTheCode()
    {
        var broker = new SpotifyAuthorizationCallbackBroker();
        using var registration = broker.Begin("expected-state");

        var handled = broker.TryComplete(
            new Uri(
                "http://127.0.0.1:13130/oauth/spotify/callback?code=authorization-code&state=wrong-state"),
            out var response);
        var error = await Assert.ThrowsAsync<SpotifyAuthorizationException>(() =>
            registration.WaitForAuthorizationCodeAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None));

        Assert.True(handled);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("state", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
