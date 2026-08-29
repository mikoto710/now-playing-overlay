using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Media.WindowTitles;

internal interface IWindowTitleCatalog
{
    IReadOnlyList<WindowTitleWindow> GetWindows();
}

internal sealed record WindowTitleWindow(WindowTitleTargetSettings Target, string Title);

internal sealed record WindowTitleCandidate(
    WindowTitleTargetSettings Target,
    string CurrentTitle,
    int MatchCount)
{
    public SourceDescriptor ToDescriptor()
    {
        return SourceDescriptor.WindowTitle(Target.InstanceId, Target.DisplayName);
    }
}

internal sealed record WindowTitleDiscoveryResult(
    IReadOnlyList<WindowTitleCandidate> Candidates,
    SourceManagerState State);
