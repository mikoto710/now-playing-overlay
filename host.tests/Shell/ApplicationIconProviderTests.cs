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
    public void LargeFrameFitsEntireCanvas()
    {
        var assembly = typeof(ApplicationIconProvider).Assembly;
        using var iconStream = assembly.GetManifestResourceStream(ApplicationIconProvider.ResourceName);
        Assert.NotNull(iconStream);

        using var reader = new BinaryReader(iconStream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var frameCount = reader.ReadUInt16();
        (uint Length, uint Offset)? largeFrame = null;

        for (var index = 0; index < frameCount; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            reader.ReadBytes(6);
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();

            if ((width == 0 ? 256 : width) == 256 && (height == 0 ? 256 : height) == 256)
            {
                largeFrame = (length, offset);
            }
        }

        Assert.True(largeFrame.HasValue, "The embedded icon does not contain a 256px frame.");
        iconStream.Position = largeFrame.Value.Offset;
        var frameBytes = reader.ReadBytes(checked((int)largeFrame.Value.Length));
        Assert.Equal(checked((int)largeFrame.Value.Length), frameBytes.Length);

        using var frameStream = new MemoryStream(frameBytes);
        using var bitmap = new Bitmap(frameStream);

        var visibleBounds = FindVisibleBounds(bitmap);

        Assert.True(visibleBounds.Left > 0);
        Assert.True(visibleBounds.Top > 0);
        Assert.True(visibleBounds.Right < bitmap.Width);
        Assert.True(visibleBounds.Bottom < bitmap.Height);
        Assert.True(visibleBounds.Width >= 192);
        Assert.True(visibleBounds.Height >= 176);
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

    private static Rectangle FindVisibleBounds(Bitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        Assert.True(right >= left && bottom >= top, "The icon frame is fully transparent.");
        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}
