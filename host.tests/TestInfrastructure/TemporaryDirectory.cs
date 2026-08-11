namespace NowPlayingOverlay.Host.Tests.TestInfrastructure;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string prefix = "now-playing-overlay-tests-")
    {
        Path = Directory.CreateTempSubdirectory(prefix).FullName;
    }

    public string Path { get; }

    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
    }
}
