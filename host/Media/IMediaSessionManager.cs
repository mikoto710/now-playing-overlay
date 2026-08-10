namespace NowPlayingOverlay.Host.Media;

internal interface IMediaSessionManager : IDisposable
{
    event EventHandler? SessionsChanged;

    IReadOnlyList<IMediaSessionAdapter> GetSessions();
}
