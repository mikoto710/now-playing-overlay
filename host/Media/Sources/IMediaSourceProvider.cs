namespace NowPlayingOverlay.Host.Media.Sources;

/// <summary>
/// A selectable provider that returns complete, single-source observations.
/// </summary>
internal interface IMediaSourceProvider : ISessionSource, ISessionSourceStatus
{
    SourceProvider Provider { get; }

    void SetSelection(SourceDescriptor? selection);
}
