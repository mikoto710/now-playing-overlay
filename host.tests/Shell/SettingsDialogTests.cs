using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Shell;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class SettingsDialogTests
{
    [Fact]
    public void SettingsIncludesASeparateOutputsPageWithAllFourGroups()
    {
        RunSta(() =>
        {
            using var dialog = new SettingsDialog(
                HostOptions.DefaultPort,
                new SourceDiscoveryResult([], SourceManagerState.Unconfigured),
                SourceSelectionSettings.WindowsMedia(instanceId: null),
                new WindowsMediaSettings(),
                SpotifyConnectionSnapshot.Disconnected,
                new AppearanceSettings(),
                (_, _) => Task.FromResult(new SourceDiscoveryResult(
                    [],
                    SourceManagerState.Unconfigured)),
                (_, _, _) => Task.FromResult(SpotifyConnectionSnapshot.Disconnected),
                _ => Task.FromResult(SpotifyConnectionSnapshot.Disconnected));

            var tabs = FindControl<TabControl>(dialog);
            var outputs = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Outputs");
            var groupNames = FindControls<GroupBox>(outputs)
                .Select(group => group.Text)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("Text", groupNames);
            Assert.Contains("JSON", groupNames);
            Assert.Contains("Artwork", groupNames);
            Assert.Contains("History", groupNames);
            Assert.Empty(dialog.SelectedOutputs.Text);
        });
    }

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

    private static T FindControl<T>(Control root)
        where T : Control
    {
        return FindControls<T>(root).First();
    }

    private static IEnumerable<T> FindControls<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindControls<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                error = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
