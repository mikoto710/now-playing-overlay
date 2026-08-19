using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class SettingsDialogTests
{
    [Fact]
    public void DormantWindowsSelectionUsesLatestDiscoveryInsteadOfInactiveState()
    {
        const string playerId = "SpotifyAB.SpotifyMusic_test!Spotify";
        var currentSource = SourceSelectionSettings.SpotifyApi();
        var discovery = new SourceDiscoveryResult(
            [SourceDescriptor.WindowsMedia(playerId)],
            SourceManagerState.Unconfigured);

        var status = SettingsDialog.BuildWindowsSelectionStatusText(
            playerId,
            currentSource,
            discovery);

        Assert.Equal("The selected player is available. Save to apply.", status);
    }

    [Fact]
    public void MissingDraftSelectionIsReportedWithoutChangingTheRuntimeSource()
    {
        const string playerId = "SpotifyAB.SpotifyMusic_test!Spotify";
        var currentSource = SourceSelectionSettings.SpotifyApi();
        var discovery = new SourceDiscoveryResult([], SourceManagerState.Unconfigured);

        var status = SettingsDialog.BuildWindowsSelectionStatusText(
            playerId,
            currentSource,
            discovery);

        Assert.Equal(
            "The selected player is not currently available. The selection will be kept.",
            status);
    }
}
