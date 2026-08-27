using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class OutputSettingsControl : UserControl
{
    private readonly Func<string, string> _renderPreview;
    private readonly CheckBox _textEnabled;
    private readonly TextBox _textPath;
    private readonly TextBox _textTemplate;
    private readonly ComboBox _noMediaBehavior;
    private readonly TextBox _textPlaceholder;
    private readonly TableLayoutPanel _textPlaceholderRow;
    private readonly TextBox _preview;
    private readonly CheckBox _jsonEnabled;
    private readonly TextBox _jsonPath;
    private readonly ComboBox _jsonFormat;
    private readonly CheckBox _artworkEnabled;
    private readonly TextBox _artworkPath;
    private readonly ComboBox _missingArtworkBehavior;
    private readonly CheckBox _historyEnabled;
    private readonly TextBox _historyPath;
    private readonly TextBox _historyTemplate;

    public OutputSettingsControl(
        OutputSettings current,
        OutputStatusSnapshot status,
        Func<string, string> renderPreview)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(status);
        current.Validate();
        _renderPreview = renderPreview ?? throw new ArgumentNullException(nameof(renderPreview));
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            MaximumSize = new Size(700, 0),
            Text = "Some files could not be updated. Check the logs for details.",
        };

        _textEnabled = new CheckBox
        {
            AutoSize = true,
            Checked = current.Text.Enabled,
            Text = "Write to TXT",
        };
        _textPath = CreatePathBox(current.Text.FilePath);
        _textTemplate = CreatePathBox(current.Text.Template);
        _textTemplate.SelectionStart = _textTemplate.TextLength;
        _noMediaBehavior = CreateEnumComboBox(
            current.Text.NoMediaBehavior,
            FormatNoMediaBehavior);
        _noMediaBehavior.MinimumSize = new Size(240, 0);
        _textPlaceholder = CreatePathBox(current.Text.NoMediaTemplate);
        _textPlaceholderRow = CreateLabeledControlRow(
            "Placeholder:",
            _textPlaceholder);
        _preview = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            MinimumSize = new Size(0, 52),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.None,
        };
        var textLayout = CreateVerticalLayout(7);
        textLayout.Controls.Add(_textEnabled);
        textLayout.Controls.Add(CreateLabeledControlRow(
            "File:",
            CreatePathRow(
                _textPath,
                (_, _) => BrowsePath(_textPath, "Text files (*.txt)|*.txt", "txt"))));
        textLayout.Controls.Add(CreateLabeledControlRow("Contents:", _textTemplate));
        textLayout.Controls.Add(CreateLabeledControlRow(
            "When nothing is playing:",
            _noMediaBehavior));
        textLayout.Controls.Add(_textPlaceholderRow);
        textLayout.Controls.Add(CreateLabeledControlRow("Preview:", _preview));
        textLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
            Text = "Fields: {title}, {artist}, {albumTitle}, {newline}",
        });
        var textGroup = CreateGroup("Text", textLayout);

        _textTemplate.TextChanged += (_, _) => UpdatePreview();
        _noMediaBehavior.SelectedIndexChanged += (_, _) => UpdatePlaceholderAvailability();
        UpdatePlaceholderAvailability();

        _jsonEnabled = new CheckBox
        {
            AutoSize = true,
            Checked = current.Json.Enabled,
            Text = "Write to JSON",
        };
        _jsonPath = CreatePathBox(current.Json.FilePath);
        _jsonFormat = CreateEnumComboBox(current.Json.Format, FormatJsonOutputFormat);
        var jsonLayout = CreateFileOutputLayout(
            _jsonEnabled,
            _jsonPath,
            (_, _) => BrowsePath(_jsonPath, "JSON files (*.json)|*.json", "json"),
            CreateLabel("Format:"),
            _jsonFormat);
        var jsonGroup = CreateGroup("JSON", jsonLayout);

        _artworkEnabled = new CheckBox
        {
            AutoSize = true,
            Checked = current.Artwork.Enabled,
            Text = "Save artwork",
        };
        _artworkPath = CreatePathBox(current.Artwork.FilePath);
        _missingArtworkBehavior = CreateEnumComboBox(
            current.Artwork.MissingArtworkBehavior,
            FormatMissingArtworkBehavior);
        var artworkLayout = CreateFileOutputLayout(
            _artworkEnabled,
            _artworkPath,
            (_, _) => BrowsePath(_artworkPath, "PNG images (*.png)|*.png", "png"),
            CreateLabel("No artwork:"),
            _missingArtworkBehavior);
        var artworkGroup = CreateGroup("Artwork", artworkLayout);

        _historyEnabled = new CheckBox
        {
            AutoSize = true,
            Checked = current.History.Enabled,
            Text = "Save track history",
        };
        _historyPath = CreatePathBox(current.History.FilePath);
        _historyTemplate = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = current.History.Template,
        };
        var historyLayout = CreateVerticalLayout(4);
        historyLayout.Controls.Add(_historyEnabled);
        historyLayout.Controls.Add(CreatePathRow(
            _historyPath,
            (_, _) => BrowsePath(_historyPath, "Text files (*.txt)|*.txt", "txt")));
        var historyTemplateRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1,
        };
        historyTemplateRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        historyTemplateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyTemplateRow.Controls.Add(CreateLabel("Line:"), 0, 0);
        historyTemplateRow.Controls.Add(_historyTemplate, 1, 0);
        historyLayout.Controls.Add(historyTemplateRow);
        historyLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            MaximumSize = new Size(680, 0),
            Text = "Adds one line when the track changes.",
        });
        var historyGroup = CreateGroup("History", historyLayout);

        var root = CreateVerticalLayout(status.IsFaulted ? 5 : 4);
        root.Padding = new Padding(12, 12, 12, 24);
        if (status.IsFaulted)
        {
            root.Controls.Add(statusLabel);
        }

        root.Controls.Add(textGroup);
        root.Controls.Add(jsonGroup);
        root.Controls.Add(artworkGroup);
        root.Controls.Add(historyGroup);
        Controls.Add(root);
        UpdatePreview();
    }

    public OutputSettings SelectedOutputs
    {
        get
        {
            var settings = new OutputSettings
            {
                Text = new TextOutputSettings
                {
                    Enabled = _textEnabled.Checked,
                    FilePath = NullIfEmpty(_textPath.Text),
                    Template = _textTemplate.Text,
                    NoMediaBehavior = GetSelectedEnum(
                        _noMediaBehavior,
                        NoMediaOutputBehavior.Clear),
                    NoMediaTemplate = _textPlaceholder.Text,
                },
                Json = new JsonOutputSettings
                {
                    Enabled = _jsonEnabled.Checked,
                    FilePath = NullIfEmpty(_jsonPath.Text),
                    Format = GetSelectedEnum(_jsonFormat, JsonOutputFormat.Compact),
                },
                Artwork = new ArtworkOutputSettings
                {
                    Enabled = _artworkEnabled.Checked,
                    FilePath = NullIfEmpty(_artworkPath.Text),
                    MissingArtworkBehavior = GetSelectedEnum(
                        _missingArtworkBehavior,
                        MissingArtworkBehavior.Delete),
                },
                History = new HistoryOutputSettings
                {
                    Enabled = _historyEnabled.Checked,
                    FilePath = NullIfEmpty(_historyPath.Text),
                    Template = _historyTemplate.Text,
                },
            };
            settings.Validate();
            return settings;
        }
    }

    private void UpdatePlaceholderAvailability()
    {
        var visible = GetSelectedEnum(
            _noMediaBehavior,
            NoMediaOutputBehavior.Clear) == NoMediaOutputBehavior.Placeholder;
        _textPlaceholder.Enabled = visible;
        _textPlaceholderRow.Visible = visible;
    }

    private void UpdatePreview()
    {
        try
        {
            var rendered = _renderPreview(_textTemplate.Text);
            _preview.Text = rendered.Length == 0 ? "(empty)" : rendered;
        }
        catch (FormatException error)
        {
            _preview.Text = error.Message;
        }
    }

    private void BrowsePath(TextBox target, string filter, string extension)
    {
        using var dialog = CreateSaveDialog(target.Text, filter, extension);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private static SaveFileDialog CreateSaveDialog(
        string currentPath,
        string filter,
        string extension)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = extension,
            Filter = filter,
            OverwritePrompt = false,
            RestoreDirectory = true,
        };
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            dialog.FileName = Path.GetFileName(currentPath);
            var directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        return dialog;
    }

    private static GroupBox CreateGroup(string text, Control content)
    {
        var group = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(8),
            Text = text,
        };
        group.Controls.Add(content);
        return group;
    }

    private static TableLayoutPanel CreateVerticalLayout(int rows)
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = rows,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < rows; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static TableLayoutPanel CreateFileOutputLayout(
        CheckBox enabled,
        TextBox path,
        EventHandler browse,
        Label optionLabel,
        Control option)
    {
        var layout = CreateVerticalLayout(3);
        layout.Controls.Add(enabled);
        layout.Controls.Add(CreatePathRow(path, browse));
        var optionRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1,
        };
        optionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        optionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        optionRow.Controls.Add(optionLabel, 0, 0);
        optionRow.Controls.Add(option, 1, 0);
        layout.Controls.Add(optionRow);
        return layout;
    }

    private static TableLayoutPanel CreatePathRow(TextBox path, EventHandler browse)
    {
        var button = CreateButton("Browse...", browse);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(path, 0, 0);
        layout.Controls.Add(button, 1, 0);
        return layout;
    }

    private static TableLayoutPanel CreateLabeledControlRow(string label, Control content)
    {
        content.Margin = Padding.Empty;
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 6, 0, 0),
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateLabel(label), 0, 0);
        layout.Controls.Add(content, 1, 0);
        return layout;
    }

    private static Button CreateButton(string text, EventHandler clicked)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = text,
        };
        button.Click += clicked;
        return button;
    }

    private static TextBox CreatePathBox(string? value)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = value ?? string.Empty,
        };
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 3, 8, 0),
            Text = text,
        };
    }

    private static ComboBox CreateEnumComboBox<T>(T selected, Func<T, string> format)
        where T : struct, Enum
    {
        var combo = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 220,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(190, 0),
        };
        combo.Items.AddRange(Enum.GetValues<T>().Cast<object>().ToArray());
        combo.Format += (_, eventArgs) =>
        {
            if (eventArgs.ListItem is T value)
            {
                eventArgs.Value = format(value);
            }
        };
        combo.SelectedItem = selected;
        return combo;
    }

    private static string FormatNoMediaBehavior(NoMediaOutputBehavior behavior)
    {
        return behavior switch
        {
            NoMediaOutputBehavior.Clear => "Clear file",
            NoMediaOutputBehavior.Placeholder => "Write placeholder",
            NoMediaOutputBehavior.KeepLast => "Keep last text",
            _ => behavior.ToString(),
        };
    }

    private static string FormatJsonOutputFormat(JsonOutputFormat format)
    {
        return format switch
        {
            JsonOutputFormat.Compact => "Compact",
            JsonOutputFormat.Indented => "Readable",
            _ => format.ToString(),
        };
    }

    private static string FormatMissingArtworkBehavior(MissingArtworkBehavior behavior)
    {
        return behavior switch
        {
            MissingArtworkBehavior.Delete => "Delete file",
            MissingArtworkBehavior.KeepLast => "Keep last image",
            _ => behavior.ToString(),
        };
    }

    private static T GetSelectedEnum<T>(ComboBox combo, T fallback)
        where T : struct, Enum
    {
        return combo.SelectedItem is T value ? value : fallback;
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

}
