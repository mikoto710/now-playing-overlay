namespace NowPlayingOverlay.Host.Shell;

internal sealed class ClipboardTextWriter
{
    internal const int RetryTimes = 30;
    internal const int RetryDelayMilliseconds = 100;

    private readonly Action<object, bool, int, int> _setDataObject;

    public ClipboardTextWriter()
        : this(Clipboard.SetDataObject)
    {
    }

    internal ClipboardTextWriter(Action<object, bool, int, int> setDataObject)
    {
        _setDataObject = setDataObject ?? throw new ArgumentNullException(nameof(setDataObject));
    }

    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var data = new DataObject(DataFormats.UnicodeText, text);

        // Clipboard ownership is transient, so extend WinForms' bounded retry window before surfacing an error.
        _setDataObject(data, true, RetryTimes, RetryDelayMilliseconds);
    }
}
