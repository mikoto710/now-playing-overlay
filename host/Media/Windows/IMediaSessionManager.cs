namespace NowPlayingOverlay.Host.Media.Windows;

internal interface IMediaSessionManager : IDisposable
{
    event EventHandler? SessionsChanged;

    IReadOnlyList<IMediaSessionAdapter> GetSessions();
}
