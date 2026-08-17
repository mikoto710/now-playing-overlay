using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Authorization;

public sealed class SpotifyCredentialStoreTests
{
    [Fact]
    public void DpapiCredentialIsAtomicallyRotatedAndDeleted()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "spotify-credentials.dat");
        var store = new SpotifyCredentialStore(path);
        var clientId = new SpotifyClientId("client-id");
        store.Save(new SpotifyStoredCredential(
            clientId,
            "first-refresh-token",
            SpotifyAuthorizationRequest.RequiredScope));

        var first = store.Load();
        var protectedBytes = File.ReadAllBytes(path);

        Assert.Equal("first-refresh-token", first!.RefreshToken);
        Assert.True(protectedBytes.AsSpan().IndexOf("first-refresh-token"u8) < 0);
        Assert.False(File.Exists(path + ".tmp"));

        store.Save(new SpotifyStoredCredential(
            clientId,
            "rotated-refresh-token",
            SpotifyAuthorizationRequest.RequiredScope));

        Assert.Equal("rotated-refresh-token", store.Load()!.RefreshToken);
        Assert.False(File.Exists(path + ".tmp"));

        store.Delete();

        Assert.Null(store.Load());
        Assert.False(File.Exists(path));
    }
}
