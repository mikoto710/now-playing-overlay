using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.WindowTitles;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class WindowTitleSettingsControl : UserControl
{
    private readonly ComboBox _window;
    private readonly Button _refresh;
    private readonly Label _status;
    private readonly Label _currentTitle;
    private readonly RadioButton _wholeTitle;
    private readonly RadioButton _splitTitle;
    private readonly TextBox _separator;
    private readonly ComboBox _splitOccurrence;
    private readonly ComboBox _leftField;
    private readonly TableLayoutPanel _splitPanel;
    private readonly Label _titlePreview;
    private readonly Label _artistPreview;
    private readonly WindowTitleTargetSettings? _rememberedTarget;

    public WindowTitleSettingsControl(
        WindowTitleSettings currentSettings,
        WindowTitleDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(discovery);
        currentSettings.Validate();
        _rememberedTarget = currentSettings.Target;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;

        _window = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(360, 0),
        };
        _window.Format += (_, args) =>
        {
            if (args.ListItem is WindowOption option)
            {
                args.Value = option.Label;
            }
        };
        _window.SelectedIndexChanged += (_, _) => UpdatePreview();

        _refresh = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Refresh",
        };
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _status = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8),
            MaximumSize = new Size(680, 0),
        };
        _currentTitle = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            MaximumSize = new Size(560, 44),
        };

        _wholeTitle = new RadioButton
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Text = "Use whole title",
        };
        _splitTitle = new RadioButton
        {
            AutoSize = true,
            Margin = new Padding(18, 0, 0, 0),
            Text = "Split title",
        };
        _wholeTitle.CheckedChanged += (_, _) => UpdateParseMode();
        _splitTitle.CheckedChanged += (_, _) => UpdateParseMode();
        var modePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 8),
            WrapContents = false,
        };
        modePanel.Controls.AddRange([_wholeTitle, _splitTitle]);

        _separator = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            MaxLength = WindowTitleSettings.MaximumSeparatorLength,
            Text = currentSettings.Separator,
        };
        _separator.TextChanged += (_, _) => UpdatePreview();
        _splitOccurrence = CreateEnumComboBox<WindowTitleSplitOccurrence>();
        _splitOccurrence.SelectedItem = currentSettings.SplitOccurrence;
        _splitOccurrence.SelectedIndexChanged += (_, _) => UpdatePreview();
        _leftField = CreateEnumComboBox<WindowTitleField>();
        _leftField.SelectedItem = currentSettings.LeftField;
        _leftField.SelectedIndexChanged += (_, _) => UpdatePreview();

        _splitPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8),
            RowCount = 3,
        };
        _splitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _splitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < _splitPanel.RowCount; index++)
        {
            _splitPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _splitPanel.Controls.Add(CreateLabel("Separator:"), 0, 0);
        _splitPanel.Controls.Add(_separator, 1, 0);
        _splitPanel.Controls.Add(CreateLabel("Split at:"), 0, 1);
        _splitPanel.Controls.Add(_splitOccurrence, 1, 1);
        _splitPanel.Controls.Add(CreateLabel("Left side:"), 0, 2);
        _splitPanel.Controls.Add(_leftField, 1, 2);

        _titlePreview = CreatePreviewLabel();
        _artistPreview = CreatePreviewLabel();
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = new Padding(8),
            RowCount = 7,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var index = 0; index < layout.RowCount; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.Controls.Add(CreateLabel("Window:"), 0, 0);
        layout.Controls.Add(_window, 1, 0);
        layout.Controls.Add(_refresh, 2, 0);
        layout.Controls.Add(_status, 0, 1);
        layout.SetColumnSpan(_status, 3);
        layout.Controls.Add(CreateLabel("Current title:"), 0, 2);
        layout.Controls.Add(_currentTitle, 1, 2);
        layout.SetColumnSpan(_currentTitle, 2);
        layout.Controls.Add(modePanel, 0, 3);
        layout.SetColumnSpan(modePanel, 3);
        layout.Controls.Add(_splitPanel, 0, 4);
        layout.SetColumnSpan(_splitPanel, 3);
        layout.Controls.Add(CreateLabel("Title:"), 0, 5);
        layout.Controls.Add(_titlePreview, 1, 5);
        layout.SetColumnSpan(_titlePreview, 2);
        layout.Controls.Add(CreateLabel("Artist:"), 0, 6);
        layout.Controls.Add(_artistPreview, 1, 6);
        layout.SetColumnSpan(_artistPreview, 2);
        Controls.Add(layout);

        _wholeTitle.Checked = currentSettings.ParseMode == WindowTitleParseMode.WholeTitle;
        _splitTitle.Checked = currentSettings.ParseMode == WindowTitleParseMode.Split;
        ApplyDiscovery(discovery);
        UpdateParseMode();
    }

    public event EventHandler? RefreshRequested;

    public WindowTitleSettings SelectedSettings
    {
        get
        {
            var settings = new WindowTitleSettings
            {
                Target = (_window.SelectedItem as WindowOption)?.Target,
                ParseMode = _splitTitle.Checked
                    ? WindowTitleParseMode.Split
                    : WindowTitleParseMode.WholeTitle,
                Separator = _separator.Text,
                SplitOccurrence = GetSelectedEnum<WindowTitleSplitOccurrence>(_splitOccurrence),
                LeftField = GetSelectedEnum<WindowTitleField>(_leftField),
            };
            settings.Validate();
            return settings;
        }
    }

    public void ApplyDiscovery(WindowTitleDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        var selectedTarget = (_window.SelectedItem as WindowOption)?.Target ?? _rememberedTarget;
        var selectedInstanceId = selectedTarget?.InstanceId;
        _window.BeginUpdate();
        try
        {
            _window.Items.Clear();
            foreach (var candidate in discovery.Candidates)
            {
                _window.Items.Add(new WindowOption(
                    candidate.Target,
                    candidate.CurrentTitle,
                    candidate.MatchCount));
            }

            if (selectedTarget is not null
                && !_window.Items.Cast<WindowOption>().Any(option =>
                    string.Equals(
                        option.Target.InstanceId,
                        selectedInstanceId,
                        StringComparison.Ordinal)))
            {
                _window.Items.Add(new WindowOption(selectedTarget, string.Empty, MatchCount: 0));
            }

            _window.SelectedItem = _window.Items.Cast<WindowOption>().FirstOrDefault(option =>
                string.Equals(option.Target.InstanceId, selectedInstanceId, StringComparison.Ordinal));
        }
        finally
        {
            _window.EndUpdate();
        }

        UpdatePreview();
    }

    public void SetRefreshing(bool refreshing)
    {
        _refresh.Enabled = !refreshing;
        _window.Enabled = !refreshing;
        if (refreshing)
        {
            _status.Text = "Refreshing windows...";
        }
    }

    private void UpdateParseMode()
    {
        _splitPanel.Visible = _splitTitle.Checked;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_window.SelectedItem is not WindowOption option)
        {
            _status.Text = "Choose a window.";
            _currentTitle.Text = string.Empty;
            SetPreview(WindowTitleParseResult.NoTrack);
            return;
        }

        _status.Text = option.MatchCount switch
        {
            0 => "The selected window is not currently available.",
            1 => "The selected window is available.",
            _ => "Multiple matching windows are open. Close the extra window before using this source.",
        };
        _currentTitle.Text = option.MatchCount == 1 ? option.CurrentTitle : string.Empty;
        if (option.MatchCount != 1)
        {
            SetPreview(WindowTitleParseResult.NoTrack);
            return;
        }

        try
        {
            SetPreview(WindowTitleParser.Parse(option.CurrentTitle, SelectedSettings));
        }
        catch (InvalidDataException)
        {
            SetPreview(WindowTitleParseResult.NoTrack);
        }
    }

    private void SetPreview(WindowTitleParseResult parsed)
    {
        _titlePreview.Text = parsed.Title ?? "<No track>";
        _artistPreview.Text = parsed.Artist ?? string.Empty;
    }

    private static ComboBox CreateEnumComboBox<T>()
        where T : struct, Enum
    {
        var combo = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = new Padding(0, 2, 0, 2),
            MinimumSize = new Size(220, 0),
        };
        combo.Items.AddRange(Enum.GetValues<T>().Cast<object>().ToArray());
        combo.Format += (_, args) =>
        {
            if (args.ListItem is T value)
            {
                args.Value = value switch
                {
                    WindowTitleSplitOccurrence.First => "First separator",
                    WindowTitleSplitOccurrence.Last => "Last separator",
                    WindowTitleField.Title => "Title",
                    WindowTitleField.Artist => "Artist",
                    _ => value.ToString(),
                };
            }
        };
        return combo;
    }

    private static T GetSelectedEnum<T>(ComboBox combo)
        where T : struct, Enum
    {
        return combo.SelectedItem is T value
            ? value
            : throw new InvalidOperationException("A Window Title option must be selected.");
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 2, 8, 2),
            Text = text,
        };
    }

    private static Label CreatePreviewLabel()
    {
        return new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2),
            MaximumSize = new Size(560, 44),
        };
    }

    private sealed record WindowOption(
        WindowTitleTargetSettings Target,
        string CurrentTitle,
        int MatchCount)
    {
        public string Label
        {
            get
            {
                if (MatchCount == 0)
                {
                    return $"{Target.DisplayName} — Not available";
                }

                if (MatchCount > 1)
                {
                    return $"{Target.DisplayName} — {MatchCount} matching windows";
                }

                const int maximumPreviewLength = 80;
                var title = CurrentTitle.Length <= maximumPreviewLength
                    ? CurrentTitle
                    : $"{CurrentTitle[..(maximumPreviewLength - 1)]}…";
                return $"{Target.DisplayName} — {title}";
            }
        }
    }
}
