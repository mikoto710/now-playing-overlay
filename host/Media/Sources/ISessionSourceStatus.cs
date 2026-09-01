namespace NowPlayingOverlay.Host.Media.Sources;

/// <summary>
/// Provides a point-in-time source status snapshot.
/// </summary>
internal interface ISessionSourceStatus
{
    SourceManagerState GetState();
}
