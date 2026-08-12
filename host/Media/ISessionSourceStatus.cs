namespace NowPlayingOverlay.Host.Media;

internal interface ISessionSourceStatus
{
    SourceManagerState GetState();
}
