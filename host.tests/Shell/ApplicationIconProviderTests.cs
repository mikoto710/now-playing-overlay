using System.Drawing;
using System.Reflection;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class ApplicationIconProviderTests
{
    [Fact]
    public void IconIsEmbeddedInHostAssembly()
    {
        var assembly = typeof(ApplicationIconProvider).Assembly;

        using var stream = assembly.GetManifestResourceStream(ApplicationIconProvider.ResourceName);

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void LoadsRequestedTraySizeAfterResourceStreamIsClosed(int size)
    {
        using var icon = ApplicationIconProvider.Load(new Size(size, size));

        Assert.Equal(new Size(size, size), icon.Size);
        using var bitmap = icon.ToBitmap();
        Assert.Equal(new Size(size, size), bitmap.Size);
    }

    [Fact]
    public void MissingEmbeddedIconReportsResourceAndAssembly()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApplicationIconProvider.Load(new Size(16, 16), assembly));

        Assert.Contains(ApplicationIconProvider.ResourceName, error.Message, StringComparison.Ordinal);
        Assert.Contains(assembly.GetName().Name!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidRequestedSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ApplicationIconProvider.Load(Size.Empty));
    }
}
