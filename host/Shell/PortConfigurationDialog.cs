namespace NowPlayingOverlay.Host.Shell;

internal sealed class PortConfigurationDialog : Form
{
    private readonly NumericUpDown _port;

    public PortConfigurationDialog(int currentPort)
    {
        Text = "Now Playing Overlay port";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 140);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var explanation = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(328, 38),
            Text = "Choose the loopback port. The new URL takes effect after restarting the app.",
        };
        var portLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 67),
            Text = "Port:",
        };
        _port = new NumericUpDown
        {
            Location = new Point(64, 64),
            Minimum = 1,
            Maximum = 65535,
            Value = currentPort,
            Width = 100,
        };
        var save = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(188, 100),
            Size = new Size(75, 27),
            Text = "Save",
        };
        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(269, 100),
            Size = new Size(75, 27),
            Text = "Cancel",
        };

        AcceptButton = save;
        CancelButton = cancel;
        Controls.AddRange([explanation, portLabel, _port, save, cancel]);
    }

    public int SelectedPort => decimal.ToInt32(_port.Value);
}
