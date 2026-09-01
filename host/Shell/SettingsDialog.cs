using System.Reflection;
using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.ControlPlane;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.WindowTitles;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Shell;

/// <summary>
/// Edits a settings draft; Spotify and Browser Player connection actions remain immediate.
/// </summary>
internal sealed class SettingsDialog : Form
{
    internal const string ProjectUrl = "https://github.com/mikoto710/now-playing-overlay";

    private readonly SourceSettingsControl _sources;
    private readonly AppearanceSettingsControl _appearance;
    private readonly OutputSettingsControl _outputs;
    private readonly Button _save;

    public SettingsDialog(
        int currentPort,
        SourceDiscoveryResult discovery,
        SourceSelectionSettings currentSource,
        WindowsMediaSettings windowsMedia,
        SpotifyConnectionSnapshot spotifyConnection,
        AppearanceSettings currentAppearance,
        Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>> refreshSources,
        Func<string, bool, CancellationToken, Task<SpotifyConnectionSnapshot>> authorizeSpotify,
        Func<CancellationToken, Task<SpotifyConnectionSnapshot>> disconnectSpotify,
        Func<string>? getBrowserPlayerConnectionCode = null,
        Func<string>? rotateBrowserPlayerConnectionCode = null,
        Action? openBrowserProducer = null,
        Action<string>? setClipboardText = null,
        OutputSettings? currentOutputs = null,
        OutputStatusSnapshot? outputStatus = null,
        Func<string, string>? renderOutputPreview = null,
        WindowTitleSettings? currentWindowTitle = null,
        WindowTitleDiscoveryResult? windowTitleDiscovery = null,
        Func<CancellationToken, Task<WindowTitleDiscoveryResult>>? refreshWindowTitles = null,
        Action? openProjectPage = null)
    {
        getBrowserPlayerConnectionCode ??= () => string.Empty;
        rotateBrowserPlayerConnectionCode ??= getBrowserPlayerConnectionCode;
        openBrowserProducer ??= () => { };
        setClipboardText ??= new ClipboardTextWriter().SetText;
        currentWindowTitle ??= new WindowTitleSettings();
        windowTitleDiscovery ??= new WindowTitleDiscoveryResult(
            [],
            SourceManagerState.Unconfigured);
        refreshWindowTitles ??= _ => Task.FromResult(
            new WindowTitleDiscoveryResult([], SourceManagerState.Unconfigured));
        currentOutputs ??= new OutputSettings();
        outputStatus ??= new OutputStatusSnapshot(
            0,
            "Outputs are ready. No output errors are recorded.");
        renderOutputPreview ??= _ => string.Empty;
        openProjectPage ??= () => { };

        _sources = new SourceSettingsControl(
            currentPort,
            discovery,
            currentSource,
            windowsMedia,
            spotifyConnection,
            currentWindowTitle,
            windowTitleDiscovery,
            new SourceSettingsActions(
                refreshSources,
                authorizeSpotify,
                disconnectSpotify,
                getBrowserPlayerConnectionCode,
                rotateBrowserPlayerConnectionCode,
                openBrowserProducer,
                setClipboardText,
                refreshWindowTitles));
        _appearance = new AppearanceSettingsControl(currentAppearance);
        _outputs = new OutputSettingsControl(
            currentOutputs,
            outputStatus,
            renderOutputPreview);

        Text = "Settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 760);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        tabs.TabPages.AddRange(
        [
            CreateTab("General", _sources, autoScroll: true),
            CreateTab("Appearance", _appearance, autoScroll: true),
            CreateTab("Outputs", _outputs),
            CreateAboutTab(openProjectPage),
        ]);

        _save = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            MinimumSize = new Size(75, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Save",
        };
        _save.Click += SaveClicked;
        _sources.BusyChanged += (_, _) => _save.Enabled = !_sources.IsBusy;
        var cancel = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0),
            MinimumSize = new Size(75, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Cancel",
        };
        var buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 16, 0, 0),
            WrapContents = false,
        };
        buttons.Controls.AddRange([_save, cancel]);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        AcceptButton = _save;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    public int SelectedPort => _sources.SelectedPort;

    public SourceProvider SelectedProvider => _sources.SelectedProvider;

    public string? SelectedInstanceId => _sources.SelectedInstanceId;

    public AppearanceSettings SelectedAppearance => _appearance.SelectedAppearance;

    public OutputSettings SelectedOutputs => _outputs.SelectedOutputs;

    public WindowTitleSettings SelectedWindowTitle => _sources.SelectedWindowTitle;

    public SettingsDraft SelectedDraft => new(
        SelectedPort,
        SelectedProvider,
        SelectedInstanceId,
        SelectedAppearance,
        SelectedOutputs,
        SelectedWindowTitle);

    internal static string CurrentVersion
    {
        get
        {
            var assembly = typeof(SettingsDialog).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                ?? assembly.GetName().Version?.ToString(3)
                ?? "Unknown";
        }
    }

    private void SaveClicked(object? sender, EventArgs args)
    {
        if (!_sources.TryValidateProviderConnection(this)
            || !_appearance.TryValidateSelection(this))
        {
            return;
        }

        try
        {
            _ = SelectedOutputs;
            _sources.ValidateSelection();
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Check Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static TabPage CreateTab(string title, Control content, bool autoScroll = false)
    {
        var tab = new TabPage(title)
        {
            AutoScroll = autoScroll,
        };
        tab.Controls.Add(content);
        return tab;
    }

    private static TabPage CreateAboutTab(Action openProjectPage)
    {
        var project = new LinkLabel
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 12),
            Text = ProjectUrl,
        };
        project.LinkClicked += (_, _) => openProjectPage();
        var layout = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(24),
            WrapContents = false,
        };
        layout.Controls.AddRange(
        [
            new Label { AutoSize = true, Text = "Now Playing Overlay" },
            new Label { AutoSize = true, Text = $"Version {CurrentVersion}" },
            new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0),
                Text = "Shows the current track and artwork from supported players.",
            },
            project,
            new Label { AutoSize = true, Text = "GNU General Public License v3.0" },
        ]);
        return CreateTab("About", layout);
    }
}
