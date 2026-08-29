using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Shell;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class SettingsDialogTests
{
    [Fact]
    public void SettingsIncludesDirectSingleTextOutputAndAllFourGroups()
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

            Assert.Equal("Settings", dialog.Text);

            var tabs = FindControl<TabControl>(dialog);
            var outputs = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Outputs");
            var groupNames = FindControls<GroupBox>(outputs)
                .Select(group => group.Text)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("Text", groupNames);
            Assert.Contains("JSON", groupNames);
            Assert.Contains("Artwork", groupNames);
            Assert.Contains("History", groupNames);
            Assert.False(dialog.SelectedOutputs.Text.Enabled);
            Assert.Equal("{nowPlaying}", dialog.SelectedOutputs.Text.Template);
            Assert.Null(dialog.SelectedOutputs.Text.FilePath);
            Assert.Empty(FindControls<DataGridView>(outputs));
            Assert.Empty(FindControls<ListBox>(outputs));
            Assert.Contains(
                FindControls<CheckBox>(outputs),
                checkBox => checkBox.Text == "Write to TXT");
            Assert.DoesNotContain(
                FindControls<Button>(outputs),
                button => button.Text.StartsWith("Add ", StringComparison.Ordinal)
                    || button.Text == "Remove");
            var visibleText = string.Join(
                ' ',
                FindControls<Control>(outputs).Select(control => control.Text));
            Assert.DoesNotContain("protocol v", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UTF-8", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("for OBS", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                FindControls<Label>(outputs),
                label => label.Text == "Fields: {title}, {artist}, {albumTitle}, {newline}");
            Assert.DoesNotContain("Insert field:", visibleText, StringComparison.Ordinal);
            var historyHelp = FindControls<Label>(outputs)
                .Single(label => label.Text == "Adds one line when the track changes.");
            Assert.True(historyHelp.Margin.Top >= 10);
            Assert.Equal(
                ScrollBars.None,
                FindControls<TextBox>(outputs).Single(textBox => textBox is
                {
                    Multiline: true,
                    ReadOnly: true,
                }).ScrollBars);

            dialog.Opacity = 0;
            dialog.Show();
            tabs.SelectedTab = outputs;
            Application.DoEvents();
            var placeholderLabel = FindControls<Label>(outputs)
                .Single(label => label.Text == "Placeholder:");
            Assert.False(placeholderLabel.Parent!.Visible);
            var noMediaCombo = FindControls<ComboBox>(outputs).Single(combo =>
                combo.Items.Contains(NoMediaOutputBehavior.Placeholder));
            Assert.True(noMediaCombo.MinimumSize.Width >= 240);
            noMediaCombo.SelectedItem = NoMediaOutputBehavior.Placeholder;
            Application.DoEvents();
            Assert.True(placeholderLabel.Parent!.Visible);

            Assert.All(
                FindControls<ComboBox>(outputs),
                combo => Assert.True(combo.MinimumSize.Width >= 190));
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

    [Fact]
    public void WindowTitlePanelUsesExplicitSplitMappingAndLivePreview()
    {
        RunSta(() =>
        {
            var target = new WindowTitleTargetSettings
            {
                ProcessName = "Player",
                ExecutablePath = @"C:\Apps\Player.exe",
                WindowClass = "PlayerWindow",
            };
            var settings = new WindowTitleSettings
            {
                Target = target,
                ParseMode = WindowTitleParseMode.Split,
                Separator = " - ",
                LeftField = WindowTitleField.Artist,
            };
            using var dialog = new SettingsDialog(
                HostOptions.DefaultPort,
                new SourceDiscoveryResult([], SourceManagerState.Unconfigured),
                SourceSelectionSettings.WindowTitle(target.InstanceId),
                new WindowsMediaSettings(),
                SpotifyConnectionSnapshot.Disconnected,
                new AppearanceSettings(),
                (_, _) => Task.FromResult(new SourceDiscoveryResult(
                    [],
                    SourceManagerState.Unconfigured)),
                (_, _, _) => Task.FromResult(SpotifyConnectionSnapshot.Disconnected),
                _ => Task.FromResult(SpotifyConnectionSnapshot.Disconnected),
                currentWindowTitle: settings,
                windowTitleDiscovery: new NowPlayingOverlay.Host.Media.WindowTitles.WindowTitleDiscoveryResult(
                    [new NowPlayingOverlay.Host.Media.WindowTitles.WindowTitleCandidate(
                        target,
                        "Artist - Song",
                        MatchCount: 1)],
                    SourceManagerState.Unconfigured));

            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();

            var group = FindControls<GroupBox>(dialog)
                .Single(candidate => candidate.Text == "Window Title");
            Assert.True(group.Visible);
            Assert.Equal(target.InstanceId, dialog.SelectedInstanceId);
            Assert.Equal(settings, dialog.SelectedWindowTitle);
            var text = FindControls<Control>(group).Select(control => control.Text).ToArray();
            Assert.Contains("Artist - Song", text);
            Assert.Contains("Song", text);
            Assert.Contains("Artist", text);
            Assert.Contains(
                FindControls<RadioButton>(group),
                radio => radio.Text == "Use whole title");
            Assert.Contains(
                FindControls<RadioButton>(group),
                radio => radio.Text == "Split title" && radio.Checked);
            var visibleText = string.Join(' ', text);
            Assert.DoesNotContain("regex", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PID", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("protocol", visibleText, StringComparison.OrdinalIgnoreCase);

            AssertVisibleLeafControlsFit(group);
            var wholeTitle = FindControls<RadioButton>(group)
                .Single(radio => radio.Text == "Use whole title");
            wholeTitle.Checked = true;
            Application.DoEvents();
            Assert.Equal(WindowTitleParseMode.WholeTitle, dialog.SelectedWindowTitle.ParseMode);
            Assert.Contains(
                FindControls<Label>(group),
                label => label.Text == "Artist - Song");

            var splitTitle = FindControls<RadioButton>(group)
                .Single(radio => radio.Text == "Split title");
            splitTitle.Checked = true;
            Application.DoEvents();
            var save = FindControls<Button>(dialog).Single(button => button.Text == "Save");
            save.PerformClick();
            Application.DoEvents();
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
        });
    }

    private static void AssertVisibleLeafControlsFit(Control container)
    {
        foreach (var control in FindControls<Control>(container).Where(control =>
            control.Visible
            && control.Controls.Count == 0))
        {
            var bounds = container.RectangleToClient(
                control.RectangleToScreen(control.ClientRectangle));
            Assert.True(
                container.ClientRectangle.Contains(bounds),
                $"{control.GetType().Name} '{control.Text}' is clipped by the Window Title group.");
        }
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
