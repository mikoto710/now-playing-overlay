namespace NowPlayingOverlay.Host.Media;

internal interface IMediaSourceProvider : ISessionSource, ISessionSourceStatus
{
    SourceProvider Provider { get; }

    void SetSelection(SourceDescriptor? selection);
}
