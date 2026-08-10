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
}
