using System.Net;
using System.Net.Sockets;

namespace NowPlayingOverlay.Host.Tests.Bootstrap;

public sealed class BootstrapAddressInUseTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(183)]
    [InlineData(10048)]
    public void RecognizesHttpListenerPortConflicts(int nativeErrorCode)
    {
        Assert.True(Program.IsAddressInUse(new HttpListenerException(nativeErrorCode)));
    }

    [Fact]
    public void RecognizesNestedSocketPortConflict()
    {
        var error = new InvalidOperationException(
            "Startup failed.",
            new SocketException((int)SocketError.AddressAlreadyInUse));

        Assert.True(Program.IsAddressInUse(error));
    }

    [Fact]
    public void RejectsUnrelatedListenerFailure()
    {
        Assert.False(Program.IsAddressInUse(new HttpListenerException(5)));
    }
}
