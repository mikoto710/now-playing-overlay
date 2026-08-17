using System.Net;
using System.Text;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Authorization;

public sealed class SpotifyAuthorizationServiceTests
{
    [Fact]
    public async Task RefreshPreservesAnOmittedRefreshTokenAndAtomicallyStoresARotation()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateConnectedStore(directory.Path);
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "access_token": "first-access-token",
                  "token_type": "Bearer",
                  "expires_in": 3600
                }
                """),
            JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "access_token": "second-access-token",
                  "token_type": "Bearer",
                  "expires_in": 3600,
                  "refresh_token": "rotated-refresh-token",
                  "scope": "user-read-currently-playing"
                }
                """),
        ]);
        using var httpClient = new HttpClient(new QueueHandler(responses));
        await using var service = new SpotifyAuthorizationService(
            store,
            new SpotifyTokenClient(httpClient),
            _ => throw new InvalidOperationException("Browser launch was not expected."));
        var clientId = new SpotifyClientId("client-id");

        var first = await service.GetAccessTokenAsync(clientId, forceRefresh: false, CancellationToken.None);
        var afterPreservedRefresh = store.Load();
        var second = await service.GetAccessTokenAsync(clientId, forceRefresh: true, CancellationToken.None);
        var afterRotation = store.Load();

        Assert.Equal("first-access-token", first.Value);
        Assert.Equal("original-refresh-token", afterPreservedRefresh!.RefreshToken);
        Assert.Equal("second-access-token", second.Value);
        Assert.Equal("rotated-refresh-token", afterRotation!.RefreshToken);
    }

    [Fact]
    public async Task InvalidGrantDeletesTheCredentialAndRequiresReauthorization()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateConnectedStore(directory.Path);
        using var httpClient = new HttpClient(new QueueHandler(new Queue<HttpResponseMessage>(
        [
            JsonResponse(
                HttpStatusCode.BadRequest,
                """
                {
                  "error": "invalid_grant",
                  "error_description": "Refresh token expired"
                }
                """),
        ])));
        await using var service = new SpotifyAuthorizationService(
            store,
            new SpotifyTokenClient(httpClient),
            _ => throw new InvalidOperationException("Browser launch was not expected."));

        var error = await Assert.ThrowsAsync<SpotifyReauthorizationRequiredException>(() =>
            service.GetAccessTokenAsync(
                new SpotifyClientId("client-id"),
                forceRefresh: false,
                CancellationToken.None));

        Assert.Contains("expired or was revoked", error.Message, StringComparison.Ordinal);
        Assert.Null(store.Load());
    }

    private static SpotifyCredentialStore CreateConnectedStore(string directory)
    {
        var store = new SpotifyCredentialStore(Path.Combine(directory, "spotify-credentials.dat"));
        store.Save(new SpotifyStoredCredential(
            new SpotifyClientId("client-id"),
            "original-refresh-token",
            SpotifyAuthorizationRequest.RequiredScope));
        return store;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class QueueHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responses.Dequeue());
        }
    }
}
