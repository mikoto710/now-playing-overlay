using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class OutputSettingsControl : UserControl
{
    private readonly Func<string, string> _renderPreview;
    private readonly DataGridView _textOutputs;
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

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            Text = "Write current metadata for OBS and other local tools. Outputs are off until you enable them and choose absolute file paths.",
        };
        var statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = status.IsFaulted ? Color.Firebrick : SystemColors.ControlText,
            Margin = new Padding(0, 6, 0, 0),
            MaximumSize = new Size(700, 0),
            Text = status.Summary,
        };

        _textOutputs = CreateTextOutputsGrid();
        foreach (var output in current.Text)
        {
            _textOutputs.Rows.Add(
                output.Enabled,
                output.Name,
                output.FilePath ?? string.Empty,
                output.Template,
                output.NoMediaBehavior,
                output.NoMediaTemplate);
        }

        _textOutputs.SelectionChanged += (_, _) => UpdatePreview();
        _textOutputs.CellValueChanged += (_, _) => UpdatePreview();
        _textOutputs.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_textOutputs.IsCurrentCellDirty)
            {
                _textOutputs.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        var addNowPlaying = CreateButton(
            "Add Now Playing",
            (_, _) => AddTextOutput("Now Playing", OutputTemplate.DefaultNowPlaying));
        var addTitle = CreateButton(
            "Add Title",
            (_, _) => AddTextOutput("Title", "{title}"));
        var removeText = CreateButton("Remove", (_, _) => RemoveSelectedTextOutput());
        var browseText = CreateButton("Browse...", (_, _) => BrowseSelectedTextOutput());
        var textButtons = CreateButtonRow(addNowPlaying, addTitle, removeText, browseText);
        _preview = new TextBox
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 52,
        };
        var textLayout = CreateVerticalLayout(4);
        textLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Add up to eight TXT files. Tokens include {nowPlaying}, {title}, {artist}, {albumTitle}, {playback}, {position}, {duration}, and {observedAt}.",
            MaximumSize = new Size(680, 0),
        });
        textLayout.Controls.Add(_textOutputs);
        textLayout.Controls.Add(textButtons);
        textLayout.Controls.Add(_preview);
        var textGroup = CreateGroup("Text", textLayout);

        _jsonEnabled = new CheckBox
        {
            AutoSize = true,
            Checked = current.Json.Enabled,
            Text = "Write protocol v3 JSON",
        };
        _jsonPath = CreatePathBox(current.Json.FilePath);
        _jsonFormat = CreateEnumComboBox<JsonOutputFormat>(current.Json.Format);
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
            Text = "Write current artwork as PNG",
        };
        _artworkPath = CreatePathBox(current.Artwork.FilePath);
        _missingArtworkBehavior = CreateEnumComboBox<MissingArtworkBehavior>(
            current.Artwork.MissingArtworkBehavior);
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
            Text = "Append track history",
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
        historyTemplateRow.Controls.Add(CreateLabel("Record:"), 0, 0);
        historyTemplateRow.Controls.Add(_historyTemplate, 1, 0);
        historyLayout.Controls.Add(historyTemplateRow);
        historyLayout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Text = "One line is appended when the committed track identity changes. Pause, timeline, and artwork updates do not add duplicates.",
        });
        var historyGroup = CreateGroup("History", historyLayout);

        var root = CreateVerticalLayout(6);
        root.Padding = new Padding(12);
        root.Controls.Add(explanation);
        root.Controls.Add(statusLabel);
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
            _textOutputs.EndEdit();
            var text = new List<TextOutputSettings>();
            foreach (DataGridViewRow row in _textOutputs.Rows)
            {
                text.Add(new TextOutputSettings
                {
                    Enabled = GetCellValue(row, 0, false),
                    Name = GetCellText(row, 1).Trim(),
                    FilePath = NullIfEmpty(GetCellText(row, 2)),
                    Template = GetCellText(row, 3),
                    NoMediaBehavior = GetCellValue(
                        row,
                        4,
                        NoMediaOutputBehavior.Clear),
                    NoMediaTemplate = GetCellText(row, 5),
                });
            }

            var settings = new OutputSettings
            {
                Text = text.ToArray(),
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

    private static DataGridView CreateTextOutputsGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            Dock = DockStyle.Top,
            Height = 190,
            Margin = new Padding(0, 8, 0, 0),
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "On",
            Width = 42,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Name",
            Width = 110,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "TXT file",
            Width = 190,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Template",
            Width = 180,
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataSource = Enum.GetValues<NoMediaOutputBehavior>(),
            HeaderText = "No media",
            Width = 90,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Placeholder",
            Width = 130,
        });
        return grid;
    }

    private void AddTextOutput(string name, string template)
    {
        if (_textOutputs.Rows.Count >= OutputSettings.MaximumTextOutputs)
        {
            MessageBox.Show(
                this,
                $"At most {OutputSettings.MaximumTextOutputs} text outputs can be configured.",
                "Text Outputs",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var index = _textOutputs.Rows.Add(
            false,
            GetUniqueTextOutputName(name),
            string.Empty,
            template,
            NoMediaOutputBehavior.Clear,
            string.Empty);
        _textOutputs.ClearSelection();
        _textOutputs.Rows[index].Selected = true;
    }

    private string GetUniqueTextOutputName(string baseName)
    {
        var names = _textOutputs.Rows
            .Cast<DataGridViewRow>()
            .Select(row => GetCellText(row, 1))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; suffix <= OutputSettings.MaximumTextOutputs; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {_textOutputs.Rows.Count + 1}";
    }

    private void RemoveSelectedTextOutput()
    {
        if (_textOutputs.SelectedRows.Count == 1)
        {
            _textOutputs.Rows.Remove(_textOutputs.SelectedRows[0]);
        }
    }

    private void BrowseSelectedTextOutput()
    {
        if (_textOutputs.SelectedRows.Count != 1)
        {
            return;
        }

        var row = _textOutputs.SelectedRows[0];
        using var dialog = CreateSaveDialog(
            GetCellText(row, 2),
            "Text files (*.txt)|*.txt",
            "txt");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            row.Cells[2].Value = dialog.FileName;
        }
    }

    private void UpdatePreview()
    {
        if (_preview is null || _textOutputs.SelectedRows.Count != 1)
        {
            if (_preview is not null)
            {
                _preview.Text = "Select a text output to preview its template.";
            }

            return;
        }

        var template = GetCellText(_textOutputs.SelectedRows[0], 3);
        try
        {
            var rendered = _renderPreview(template);
            _preview.Text = rendered.Length == 0 ? "(empty for the current state)" : rendered;
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

    private static FlowLayoutPanel CreateButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 8, 0, 0),
            WrapContents = false,
        };
        row.Controls.AddRange(buttons);
        return row;
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

    private static ComboBox CreateEnumComboBox<T>(T selected)
        where T : struct, Enum
    {
        var combo = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = Padding.Empty,
        };
        combo.Items.AddRange(Enum.GetValues<T>().Cast<object>().ToArray());
        combo.SelectedItem = selected;
        return combo;
    }

    private static T GetCellValue<T>(DataGridViewRow row, int index, T fallback)
    {
        return row.Cells[index].Value is T value ? value : fallback;
    }

    private static string GetCellText(DataGridViewRow row, int index)
    {
        return row.Cells[index].Value?.ToString() ?? string.Empty;
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
