using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Tests.Configuration;

public sealed class HostOptionsTests
{
    [Fact]
    public void DefaultsKeepTheDocumentedLoopbackServiceLimits()
    {
        var options = new HostOptions();

        options.Validate();

        Assert.Equal(10598, options.Port);
        Assert.Equal("127.0.0.1", HostOptions.AllowedHost);
        Assert.InRange(options.MaximumSseConnections, 1, options.MaximumConcurrentConnections);
        Assert.True(options.MaximumRequestHeaderCount > 0);
        Assert.True(options.MaximumRequestHeadersTotalSize > 0);
        Assert.True(options.PortRebindGracePeriod > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsInvalidPort(int port)
    {
        var options = new HostOptions { Port = port };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void RejectsSseLimitAboveConnectionLimit()
    {
        var options = new HostOptions
        {
            MaximumConcurrentConnections = 2,
            MaximumSseConnections = 3,
        };

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
