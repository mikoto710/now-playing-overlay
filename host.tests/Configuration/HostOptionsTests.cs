using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Tests.Configuration;

public sealed class HostOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsInvalidPort(int port)
    {
        var options = new HostOptions { Port = port };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void CommandLinePortOverridesThePersistedPort()
    {
        var options = HostOptionsLoader.Load(
        [
            "--Host:Port=13130",
            "--Logging:LogLevel:Default=Warning",
        ], persistedPort: 12000);

        Assert.Equal(13130, options.Port);
    }

    [Fact]
    public void RejectsMalformedHostArguments()
    {
        Assert.Throws<ArgumentException>(() => HostOptionsLoader.Load(["--Host:Port"]));
        Assert.Throws<ArgumentException>(() => HostOptionsLoader.Load(["--Host:Port=not-a-port"]));
        Assert.Throws<ArgumentException>(() => HostOptionsLoader.Load(["--Host:MaximumConcurrentConnections=9"]));
    }
}
