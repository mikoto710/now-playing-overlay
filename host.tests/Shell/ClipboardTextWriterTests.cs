using System.Windows.Forms;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class ClipboardTextWriterTests
{
    [Fact]
    public void WritesUnicodeTextWithExtendedBoundedRetryWindow()
    {
        object? capturedData = null;
        bool? capturedCopy = null;
        int? capturedRetryTimes = null;
        int? capturedRetryDelay = null;
        var writer = new ClipboardTextWriter((data, copy, retryTimes, retryDelay) =>
        {
            capturedData = data;
            capturedCopy = copy;
            capturedRetryTimes = retryTimes;
            capturedRetryDelay = retryDelay;
        });

        writer.SetText("http://127.0.0.1:13130/NowPlaying.html");

        var dataObject = Assert.IsType<DataObject>(capturedData);
        Assert.True(dataObject.TryGetData<string>(DataFormats.UnicodeText, out var copiedText));
        Assert.Equal("http://127.0.0.1:13130/NowPlaying.html", copiedText);
        Assert.True(capturedCopy);
        Assert.Equal(ClipboardTextWriter.RetryTimes, capturedRetryTimes);
        Assert.Equal(ClipboardTextWriter.RetryDelayMilliseconds, capturedRetryDelay);
    }
}
