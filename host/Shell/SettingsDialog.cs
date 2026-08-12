using System.Windows.Forms;
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
    private readonly CancellationTokenSource _shutdown = new();
    private string? _selectedSourceAppUserModelId;
    private bool _hasPendingSourceSelection;

    public SettingsDialog(
        int currentPort,
        SourceDiscoveryResult discovery,
        Func<CancellationToken, Task<SourceDiscoveryResult>> refreshSources)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _refreshSources = refreshSources ?? throw new ArgumentNullException(nameof(refreshSources));
        Text = "Now Playing Overlay Settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Padding = new Padding(16),
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var explanation = new Label
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

        layout.Controls.Add(explanation, 0, 0);
        layout.SetColumnSpan(explanation, 3);
        layout.Controls.Add(portLabel, 0, 1);
        layout.Controls.Add(_port, 1, 1);
        layout.SetColumnSpan(_port, 2);
        layout.Controls.Add(sourceLabel, 0, 2);
        layout.Controls.Add(_source, 1, 2);
        layout.Controls.Add(_refresh, 2, 2);
        layout.Controls.Add(_sourceStatus, 0, 3);
        layout.SetColumnSpan(_sourceStatus, 3);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 3);

        AcceptButton = _save;
        CancelButton = cancel;
        Controls.Add(layout);
        ApplyDiscovery(discovery);
    }

    public int SelectedPort => decimal.ToInt32(_port.Value);

    public string? SelectedSourceAppUserModelId =>
        (_source.SelectedItem as SourceOption)?.SourceAppUserModelId;

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

    private sealed record SourceOption(string? SourceAppUserModelId, string Label);
}
