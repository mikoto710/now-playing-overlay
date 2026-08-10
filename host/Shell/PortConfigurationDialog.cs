namespace NowPlayingOverlay.Host.Shell;

internal sealed class PortConfigurationDialog : Form
{
    private readonly NumericUpDown _port;

    public PortConfigurationDialog(int currentPort)
    {
        Text = "Now Playing Overlay Port";
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
            ColumnCount = 2,
            Padding = new Padding(16),
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var explanation = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(420, 0),
            Text = "Choose the loopback port. The new URL takes effect after restarting the app.",
        };
        var portLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0),
            Text = "Port:",
        };
        _port = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Minimum = 1,
            MinimumSize = new Size(120, 0),
            Maximum = 65535,
            Value = currentPort,
        };
        var save = new Button
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
        buttons.Controls.AddRange([save, cancel]);

        layout.Controls.Add(explanation, 0, 0);
        layout.SetColumnSpan(explanation, 2);
        layout.Controls.Add(portLabel, 0, 1);
        layout.Controls.Add(_port, 1, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    public int SelectedPort => decimal.ToInt32(_port.Value);
}
