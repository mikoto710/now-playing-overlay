using System.Runtime.InteropServices;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class ClipboardTextWriter
{
    internal const int AttemptCount = 2;
    internal const int RetryTimes = 10;
    internal const int RetryDelayMilliseconds = 100;
    internal const int DelayBetweenAttemptsMilliseconds = 250;

    private readonly Action<object, bool, int, int> _setDataObject;
    private readonly Action<int> _wait;

    public ClipboardTextWriter()
        : this(Clipboard.SetDataObject, Thread.Sleep)
    {
    }

    internal ClipboardTextWriter(
        Action<object, bool, int, int> setDataObject,
        Action<int>? wait = null)
    {
        _setDataObject = setDataObject ?? throw new ArgumentNullException(nameof(setDataObject));
        _wait = wait ?? Thread.Sleep;
    }

    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        for (var attempt = 1; attempt <= AttemptCount; attempt++)
        {
            var data = new DataObject(DataFormats.UnicodeText, text);
            try
            {
                _setDataObject(data, true, RetryTimes, RetryDelayMilliseconds);
                return;
            }
            catch (ExternalException) when (attempt < AttemptCount)
            {
                // A failed OLE call can leave the next complete operation able to acquire the clipboard.
                _wait(DelayBetweenAttemptsMilliseconds);
            }
        }
    }
}
