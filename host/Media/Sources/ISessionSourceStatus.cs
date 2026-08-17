namespace NowPlayingOverlay.Host.Media.Sources;

internal interface ISessionSourceStatus
{
    SourceManagerState GetState();
}
