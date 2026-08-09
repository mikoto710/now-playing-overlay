namespace NowPlayingOverlay.Host.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void HostProjectIsReferencedByTheTestAssembly()
    {
        Assert.Equal("NowPlayingOverlay.Host", typeof(Program).Assembly.GetName().Name);
    }
}
