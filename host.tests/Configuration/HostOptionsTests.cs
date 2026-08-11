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
        Assert.Equal(SessionSourceKind.Windows, options.SessionSource);
        Assert.Equal(WebAssetMode.Embedded, options.WebAssetMode);
        Assert.False(options.RunFakeScenario);
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
    public void RejectsFakeScenarioWithWindowsSource()
    {
        var options = new HostOptions { RunFakeScenario = true };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void RejectsUnknownWebAssetMode()
    {
        var options = new HostOptions { WebAssetMode = (WebAssetMode)99 };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void RejectsBlankDevelopmentWebRoot()
    {
        var options = new HostOptions { DevelopmentWebRoot = " " };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void CommandLineValuesOverrideThePersistedPortAndParseAllSupportedTypes()
    {
        var options = HostOptionsLoader.Load(
        [
            "--Host:Port=13130",
            "--Host:MaximumConcurrentConnections=9",
            "--Host:RequestHeadersTimeout=00:00:03",
            "--Host:PortRebindGracePeriod=00:00:00.250",
            "--Host:SessionSource=Fake",
            "--Host:RunFakeScenario=true",
            "--Host:WebAssetMode=Development",
            "--Host:DevelopmentWebRoot=web-output",
            "--Logging:LogLevel:Default=Warning",
        ], persistedPort: 12000);

        Assert.Equal(13130, options.Port);
        Assert.Equal(9, options.MaximumConcurrentConnections);
        Assert.Equal(TimeSpan.FromSeconds(3), options.RequestHeadersTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.PortRebindGracePeriod);
        Assert.Equal(SessionSourceKind.Fake, options.SessionSource);
        Assert.True(options.RunFakeScenario);
        Assert.Equal(WebAssetMode.Development, options.WebAssetMode);
        Assert.Equal("web-output", options.DevelopmentWebRoot);
    }

    [Fact]
    public void RejectsMalformedHostArguments()
    {
        Assert.Throws<ArgumentException>(() => HostOptionsLoader.Load(["--Host:Port"]));
        Assert.Throws<ArgumentException>(() => HostOptionsLoader.Load(["--Host:Port=not-a-port"]));
        Assert.Throws<ArgumentException>(() => HostOptionsLoader.Load(["--Host:RunFakeScenario=sometimes"]));
    }
}
