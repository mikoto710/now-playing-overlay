namespace NowPlayingOverlay.Host.Media.Sources;

internal interface IMediaSourceProvider : ISessionSource, ISessionSourceStatus
{
    SourceProvider Provider { get; }

    void SetSelection(SourceDescriptor? selection);
}
