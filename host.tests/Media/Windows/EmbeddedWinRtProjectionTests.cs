using Windows.Media.Control;

namespace NowPlayingOverlay.Host.Tests.Media.Windows;

public sealed class EmbeddedWinRtProjectionTests
{
    [Fact]
    public void ProjectionDoesNotReferenceFullWindowsRuntimeAssemblies()
    {
        var projectionAssembly = typeof(GlobalSystemMediaTransportControlsSessionManager).Assembly;
        var referencedAssemblies = projectionAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Equal("NowPlayingOverlay.WinRT", projectionAssembly.GetName().Name);
        Assert.DoesNotContain("Microsoft.Windows.SDK.NET", referencedAssemblies);
        Assert.DoesNotContain("WinRT.Runtime", referencedAssemblies);
    }
}
