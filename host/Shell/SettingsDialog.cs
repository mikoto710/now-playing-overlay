using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class SettingsDialog : Form
{
    private readonly Func<CancellationToken, Task<SourceDiscoveryResult>> _refreshSources;
    private readonly NumericUpDown _port;
    private readonly ComboBox _source;
    private readonly Label _sourceStatus;
    private readonly Button _refresh;
    private readonly Button _save;
    private readonly RadioButton _defaultAppearance;
    private readonly RadioButton _customAppearance;
    private readonly Button _artistColor;
    private readonly Button _trackColor;
    private readonly Button _backgroundColor;
    private readonly NumericUpDown _backgroundOpacity;
    private readonly NumericUpDown _cornerRadius;
    private readonly CancellationTokenSource _shutdown = new();
    private CustomAppearanceSettings _customAppearanceDraft;
    private string? _selectedSourceAppUserModelId;
    private bool _hasPendingSourceSelection;

    public SettingsDialog(
        int currentPort,
        SourceDiscoveryResult discovery,
        AppearanceSettings currentAppearance,
        Func<CancellationToken, Task<SourceDiscoveryResult>> refreshSources)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(currentAppearance);
        currentAppearance.Validate();
        _customAppearanceDraft = currentAppearance.Custom;
        _refreshSources = refreshSources ?? throw new ArgumentNullException(nameof(refreshSources));
        Text = "Now Playing Overlay Settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var generalLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 4,
        };
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var generalExplanation = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(560, 0),
            Text = "Choose the loopback port and an exact Windows Media player session. Player IDs are read from Windows and saved without guessing or automatic fallback.",
        };
        var portLabel = CreateLabel("Port:");
        _port = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Minimum = 1,
            MinimumSize = new Size(160, 0),
            Maximum = 65535,
            Value = currentPort,
        };
        var sourceLabel = CreateLabel("Player:");
        _source = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(360, 0),
        };
        _source.Format += (_, args) =>
        {
            if (args.ListItem is SourceOption option)
            {
                args.Value = option.Label;
            }
        };
        _refresh = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Refresh",
        };
        _refresh.Click += RefreshClicked;
        _sourceStatus = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(560, 0),
        };

        generalLayout.Controls.Add(generalExplanation, 0, 0);
        generalLayout.SetColumnSpan(generalExplanation, 3);
        generalLayout.Controls.Add(portLabel, 0, 1);
        generalLayout.Controls.Add(_port, 1, 1);
        generalLayout.SetColumnSpan(_port, 2);
        generalLayout.Controls.Add(sourceLabel, 0, 2);
        generalLayout.Controls.Add(_source, 1, 2);
        generalLayout.Controls.Add(_refresh, 2, 2);
        generalLayout.Controls.Add(_sourceStatus, 0, 3);
        generalLayout.SetColumnSpan(_sourceStatus, 3);

        var appearanceLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 9,
        };
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < appearanceLayout.RowCount; row++)
        {
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var appearanceExplanation = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(560, 0),
            Text = "Choose Default to preserve the product style, or Custom to change the first supported appearance values.",
        };
        _defaultAppearance = new RadioButton
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Text = "Default",
        };
        _customAppearance = new RadioButton
        {
            AutoSize = true,
            Margin = new Padding(16, 0, 0, 0),
            Text = "Custom",
        };
        _defaultAppearance.CheckedChanged += (_, _) =>
        {
            if (_defaultAppearance.Checked)
            {
                SelectDefaultAppearance();
            }
        };
        _customAppearance.CheckedChanged += (_, _) =>
        {
            if (_customAppearance.Checked)
            {
                SelectCustomAppearance();
            }
        };
        var presets = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            WrapContents = false,
        };
        presets.Controls.AddRange([_defaultAppearance, _customAppearance]);

        _artistColor = CreateColorButton(_customAppearanceDraft.ArtistColor, EditArtistColor);
        _trackColor = CreateColorButton(_customAppearanceDraft.TrackColor, EditTrackColor);
        _backgroundColor = CreateColorButton(
            _customAppearanceDraft.BackgroundColor,
            EditBackgroundColor);
        _backgroundOpacity = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Maximum = 100,
            Minimum = 0,
            MinimumSize = new Size(160, 0),
            Value = _customAppearanceDraft.BackgroundOpacityPercent,
        };
        _cornerRadius = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Maximum = 35,
            Minimum = 0,
            MinimumSize = new Size(160, 0),
            Value = _customAppearanceDraft.CornerRadius,
        };
        var resetAppearance = new Button
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Reset to Default",
        };
        resetAppearance.Click += (_, _) => ResetAppearance();
        var reloadNote = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
            MaximumSize = new Size(560, 0),
            Text = "Saved appearance changes apply when Preview or OBS reloads the overlay page.",
        };

        appearanceLayout.Controls.Add(appearanceExplanation, 0, 0);
        appearanceLayout.SetColumnSpan(appearanceExplanation, 3);
        appearanceLayout.Controls.Add(CreateLabel("Style:"), 0, 1);
        appearanceLayout.Controls.Add(presets, 1, 1);
        appearanceLayout.SetColumnSpan(presets, 2);
        AddAppearanceRow(appearanceLayout, 2, "Artist color:", _artistColor);
        AddAppearanceRow(appearanceLayout, 3, "Track color:", _trackColor);
        AddAppearanceRow(appearanceLayout, 4, "Background color:", _backgroundColor);
        AddAppearanceRow(appearanceLayout, 5, "Background opacity:", _backgroundOpacity, "%");
        AddAppearanceRow(appearanceLayout, 6, "Corner radius:", _cornerRadius, "px");
        appearanceLayout.Controls.Add(resetAppearance, 1, 7);
        appearanceLayout.SetColumnSpan(resetAppearance, 2);
        appearanceLayout.Controls.Add(reloadNote, 0, 8);
        appearanceLayout.SetColumnSpan(reloadNote, 3);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        var generalTab = new TabPage("General");
        generalTab.Controls.Add(generalLayout);
        var appearanceTab = new TabPage("Appearance");
        appearanceTab.Controls.Add(appearanceLayout);
        tabs.TabPages.AddRange([generalTab, appearanceTab]);

        _save = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.OK,
            Margin = Padding.Empty,
            MinimumSize = new Size(75, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Save",
        };
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
        _defaultAppearance.Checked = currentAppearance.Preset == AppearancePreset.Default;
        _customAppearance.Checked = currentAppearance.Preset == AppearancePreset.Custom;
        ApplyDiscovery(discovery);
    }

    public int SelectedPort => decimal.ToInt32(_port.Value);

    public string? SelectedSourceAppUserModelId =>
        (_source.SelectedItem as SourceOption)?.SourceAppUserModelId;

    public AppearanceSettings SelectedAppearance
    {
        get
        {
            var custom = _customAppearance.Checked
                ? ReadAppearanceControls()
                : _customAppearanceDraft;
            var appearance = new AppearanceSettings
            {
                Preset = _customAppearance.Checked
                    ? AppearancePreset.Custom
                    : AppearancePreset.Default,
                Custom = custom,
            };
            appearance.Validate();
            return appearance;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void RefreshClicked(object? sender, EventArgs args)
    {
        _selectedSourceAppUserModelId = SelectedSourceAppUserModelId;
        _hasPendingSourceSelection = true;
        SetRefreshState(refreshing: true);
        try
        {
            var discovery = await _refreshSources(_shutdown.Token);
            ApplyDiscovery(discovery);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _sourceStatus.Text = $"Could not refresh Windows Media players: {error.Message}";
        }
        finally
        {
            if (!IsDisposed && !_shutdown.IsCancellationRequested)
            {
                SetRefreshState(refreshing: false);
            }
        }
    }

    private void ApplyDiscovery(SourceDiscoveryResult discovery)
    {
        var selected = _hasPendingSourceSelection
            ? _selectedSourceAppUserModelId
            : discovery.State.ActiveSource?.Key.InstanceId;
        var options = new List<SourceOption>
        {
            new(null, "(Not configured)"),
        };
        options.AddRange(discovery.Sources.Select(source =>
            new SourceOption(source.Key.InstanceId, source.Key.InstanceId)));
        if (selected is not null
            && options.All(option => !string.Equals(
                option.SourceAppUserModelId,
                selected,
                StringComparison.Ordinal)))
        {
            options.Add(new SourceOption(selected, $"{selected} (currently unavailable)"));
        }

        _source.BeginUpdate();
        try
        {
            _source.Items.Clear();
            _source.Items.AddRange(options.Cast<object>().ToArray());
            _source.SelectedIndex = Math.Max(
                0,
                options.FindIndex(option => string.Equals(
                    option.SourceAppUserModelId,
                    selected,
                    StringComparison.Ordinal)));
        }
        finally
        {
            _source.EndUpdate();
        }

        _sourceStatus.Text = BuildStatusText(discovery.State);
    }

    internal static string BuildStatusText(SourceManagerState state)
    {
        return state.Status switch
        {
            SourceStatus.Unconfigured => "No player is selected.",
            SourceStatus.Starting => "Windows Media player discovery is starting.",
            SourceStatus.Available => "The selected player is available.",
            SourceStatus.Unavailable when state.Reason == SourceStatusReason.Missing =>
                "The selected player is not currently available. The selection will be kept.",
            SourceStatus.Unavailable when state.Reason == SourceStatusReason.Ambiguous =>
                "Multiple exact sessions match this player and no single playing session can be selected.",
            SourceStatus.Unavailable when state.Reason == SourceStatusReason.PlatformUnavailable =>
                "Windows Media sessions are temporarily unavailable.",
            SourceStatus.Unavailable => "The selected player is unavailable.",
            SourceStatus.Faulted => "Player discovery faulted. Open the logs for details.",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private void EditArtistColor(object? sender, EventArgs args)
    {
        EditColor(_artistColor);
    }

    private void EditTrackColor(object? sender, EventArgs args)
    {
        EditColor(_trackColor);
    }

    private void EditBackgroundColor(object? sender, EventArgs args)
    {
        EditColor(_backgroundColor);
    }

    private void EditColor(Button button)
    {
        using var dialog = new ColorDialog
        {
            Color = ColorTranslator.FromHtml(button.Text),
            FullOpen = true,
            SolidColorOnly = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetColorButton(button, ToHexColor(dialog.Color));
        }
    }

    private void ResetAppearance()
    {
        var defaults = new CustomAppearanceSettings();
        ApplyAppearanceControls(defaults);
        _customAppearanceDraft = defaults;
        _defaultAppearance.Checked = true;
        SetAppearanceControlsEnabled(enabled: false);
    }

    private void SelectDefaultAppearance()
    {
        _customAppearanceDraft = ReadAppearanceControls();
        ApplyAppearanceControls(new CustomAppearanceSettings());
        SetAppearanceControlsEnabled(enabled: false);
    }

    private void SelectCustomAppearance()
    {
        ApplyAppearanceControls(_customAppearanceDraft);
        SetAppearanceControlsEnabled(enabled: true);
    }

    private CustomAppearanceSettings ReadAppearanceControls()
    {
        return new CustomAppearanceSettings
        {
            ArtistColor = _artistColor.Text,
            TrackColor = _trackColor.Text,
            BackgroundColor = _backgroundColor.Text,
            BackgroundOpacityPercent = decimal.ToInt32(_backgroundOpacity.Value),
            CornerRadius = decimal.ToInt32(_cornerRadius.Value),
            FontFamily = _customAppearanceDraft.FontFamily,
            ArtistFontSize = _customAppearanceDraft.ArtistFontSize,
            ArtistFontWeight = _customAppearanceDraft.ArtistFontWeight,
            TrackFontSize = _customAppearanceDraft.TrackFontSize,
            TrackFontWeight = _customAppearanceDraft.TrackFontWeight,
        };
    }

    private void ApplyAppearanceControls(CustomAppearanceSettings appearance)
    {
        appearance.Validate();
        SetColorButton(_artistColor, appearance.ArtistColor);
        SetColorButton(_trackColor, appearance.TrackColor);
        SetColorButton(_backgroundColor, appearance.BackgroundColor);
        _backgroundOpacity.Value = appearance.BackgroundOpacityPercent;
        _cornerRadius.Value = appearance.CornerRadius;
    }

    private void SetAppearanceControlsEnabled(bool enabled)
    {
        _artistColor.Enabled = enabled;
        _trackColor.Enabled = enabled;
        _backgroundColor.Enabled = enabled;
        _backgroundOpacity.Enabled = enabled;
        _cornerRadius.Enabled = enabled;
    }

    private void SetRefreshState(bool refreshing)
    {
        _refresh.Enabled = !refreshing;
        _save.Enabled = !refreshing;
        _source.Enabled = !refreshing;
        if (refreshing)
        {
            _sourceStatus.Text = "Refreshing Windows Media players...";
        }
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
            Text = text,
        };
    }

    private static Button CreateColorButton(string color, EventHandler click)
    {
        var button = new Button
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            MinimumSize = new Size(160, 0),
            Padding = new Padding(8, 2, 8, 2),
            UseVisualStyleBackColor = false,
        };
        SetColorButton(button, color);
        button.Click += click;
        return button;
    }

    private static void SetColorButton(Button button, string color)
    {
        var parsed = ColorTranslator.FromHtml(color);
        button.Text = color;
        button.BackColor = parsed;
        button.ForeColor = parsed.GetBrightness() < 0.5 ? Color.White : Color.Black;
    }

    private static string ToHexColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void AddAppearanceRow(
        TableLayoutPanel layout,
        int row,
        string label,
        Control control,
        string? suffix = null)
    {
        layout.Controls.Add(CreateLabel(label), 0, row);
        layout.Controls.Add(control, 1, row);
        if (suffix is not null)
        {
            layout.Controls.Add(CreateLabel(suffix), 2, row);
        }
    }

    private sealed record SourceOption(string? SourceAppUserModelId, string Label);
}
