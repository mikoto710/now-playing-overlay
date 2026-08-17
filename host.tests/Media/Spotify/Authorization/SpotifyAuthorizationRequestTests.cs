using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Authorization;

public sealed class SpotifyAuthorizationRequestTests
{
    [Fact]
    public void BuildsTheRequiredPkceAuthorizationRequestWithoutAClientSecret()
    {
        var request = SpotifyAuthorizationRequest.Create(
            new SpotifyClientId("client-id"),
            new Uri("http://127.0.0.1:54321/callback"),
            "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            "state-value");

        Assert.Equal("https", request.AuthorizationUri.Scheme);
        Assert.Equal("accounts.spotify.com", request.AuthorizationUri.Host);
        Assert.Contains("response_type=code", request.AuthorizationUri.Query, StringComparison.Ordinal);
        Assert.Contains("client_id=client-id", request.AuthorizationUri.Query, StringComparison.Ordinal);
        Assert.Contains(
            "redirect_uri=http%3A%2F%2F127.0.0.1%3A54321%2Fcallback",
            request.AuthorizationUri.Query,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "scope=user-read-currently-playing",
            request.AuthorizationUri.Query,
            StringComparison.Ordinal);
        Assert.Contains("state=state-value", request.AuthorizationUri.Query, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", request.AuthorizationUri.Query, StringComparison.Ordinal);
        Assert.Contains(
            "code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            request.AuthorizationUri.Query,
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret", request.AuthorizationUri.Query, StringComparison.OrdinalIgnoreCase);
    }
}
