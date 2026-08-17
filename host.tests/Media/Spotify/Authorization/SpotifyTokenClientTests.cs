using System.Net;
using System.Text;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Authorization;

public sealed class SpotifyTokenClientTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExchangesTheAuthorizationCodeWithThePkceFormOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var httpClient = new HttpClient(new StubHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "access_token": "access-token",
                  "token_type": "Bearer",
                  "expires_in": 3600,
                  "refresh_token": "refresh-token",
                  "scope": "user-read-currently-playing"
                }
                """);
        }));
        var client = new SpotifyTokenClient(httpClient, new FixedTimeProvider());

        var result = await client.ExchangeAuthorizationCodeAsync(
            new SpotifyClientId("client-id"),
            "authorization-code",
            new Uri("http://127.0.0.1:54321/oauth/spotify/callback"),
            "code-verifier-value-that-is-long-enough-for-pkce-1234567890",
            CancellationToken.None);
        var form = ParseForm(capturedBody!);

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://accounts.spotify.com/api/token", capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Null(capturedRequest.Headers.Authorization);
        Assert.Equal("client-id", form["client_id"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("authorization-code", form["code"]);
        Assert.Equal(
            "http://127.0.0.1:54321/oauth/spotify/callback",
            form["redirect_uri"]);
        Assert.Equal(
            "code-verifier-value-that-is-long-enough-for-pkce-1234567890",
            form["code_verifier"]);
        Assert.DoesNotContain("client_secret", form.Keys);
        Assert.Equal("access-token", result.AccessToken.Value);
        Assert.Equal(Now.AddHours(1), result.AccessToken.ExpiresAtUtc);
        Assert.Equal("refresh-token", result.RefreshToken);
    }

    private static IReadOnlyDictionary<string, string> ParseForm(string body)
    {
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
                StringComparer.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
