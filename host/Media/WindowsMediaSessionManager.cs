using Windows.Media.Control;

namespace NowPlayingOverlay.Host.Media;

internal sealed class WindowsMediaSessionManager : IMediaSessionManager
{
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private bool _disposed;

    public WindowsMediaSessionManager(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _manager.SessionsChanged += OnSessionsChanged;
    }

    public event EventHandler? SessionsChanged;

    public IReadOnlyList<IMediaSessionAdapter> GetSessions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _manager.GetSessions()
            .Select(session => (IMediaSessionAdapter)new WindowsMediaSessionAdapter(session))
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.SessionsChanged -= OnSessionsChanged;
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }
}
