using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Media.External;

public sealed class IngestKeyStoreTests
{
    [Fact]
    public void LoadOrCreatePersistsDpapiProtectedKeyAndRotateReplacesIt()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "external-ingest-key.dat");
        var store = new IngestKeyStore(path);
        using var created = store.LoadOrCreate();
        var createdValue = created.Export();
        var rawKey = Convert.FromBase64String(
            createdValue.Replace('-', '+').Replace('_', '/') + "=");
        var protectedBytes = File.ReadAllBytes(path);

        using var loaded = store.LoadOrCreate();

        Assert.Equal(IngestKey.EncodedLength, createdValue.Length);
        Assert.Equal(createdValue, loaded.Export());
        Assert.True(protectedBytes.AsSpan().IndexOf(rawKey) < 0);
        Assert.False(File.Exists(path + ".tmp"));

        using var rotated = store.Rotate();

        Assert.NotEqual(createdValue, rotated.Export());
        using var reloaded = store.LoadOrCreate();
        Assert.Equal(rotated.Export(), reloaded.Export());
        Assert.False(File.Exists(path + ".tmp"));

        store.Delete();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AuthorizationComparisonRequiresExactCanonicalBearerToken()
    {
        using var key = IngestKey.Generate();
        var token = key.Export();

        Assert.True(key.MatchesAuthorization($"Bearer {token}"));
        Assert.True(key.MatchesAuthorization($"bearer {token}"));
        Assert.False(key.MatchesAuthorization(token));
        Assert.False(key.MatchesAuthorization($"Bearer  {token}"));
        Assert.False(key.MatchesAuthorization($"Bearer {token}="));
        Assert.False(key.MatchesAuthorization($"Bearer {new string('a', IngestKey.EncodedLength)}"));
    }

    [Fact]
    public void CorruptProtectedDocumentFailsClosed()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "external-ingest-key.dat");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var store = new IngestKeyStore(path);

        Assert.Throws<InvalidDataException>(() => store.LoadOrCreate());
    }
}
