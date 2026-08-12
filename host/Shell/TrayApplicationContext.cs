using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayMenuController _controller;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _applicationIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private bool _faultNotificationShown;
    private bool _disposed;

    public TrayApplicationContext(
        TrayMenuController controller,
        ILogger<TrayApplicationContext> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            CreateMenuItem("Copy OBS URL", CopyOverlayUrl),
            CreateOverlayPreviewMenu(),
            CreateMenuItem("Open Logs", OpenLogs),
            CreateMenuItem("Configure Port...", ConfigurePort),
            new ToolStripSeparator(),
            CreateMenuItem("Exit", RequestExit),
        ]);
        _applicationIcon = ApplicationIconProvider.LoadSmallIcon();
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _applicationIcon,
            Text = "Now Playing Overlay",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) =>
            OpenOverlayPreview(TrayMenuController.OverlayPreviewOptions[0]);
        SystemEvents.SessionEnding += OnSessionEnding;
        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeResources();
        }

        base.Dispose(disposing);
    }

    private ToolStripMenuItem CreateMenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private ToolStripMenuItem CreateOverlayPreviewMenu()
    {
        var item = new ToolStripMenuItem("Open Overlay Preview");
        foreach (var option in TrayMenuController.OverlayPreviewOptions)
        {
            item.DropDownItems.Add(
                CreateMenuItem(option.MenuText, () => OpenOverlayPreview(option)));
        }

        return item;
    }

    private void RefreshStatus()
    {
        var status = _controller.GetStatus();
        _statusItem.Text = status.Text;
        if (status.IsFaulted && !_faultNotificationShown)
        {
            _faultNotificationShown = true;
            _notifyIcon.ShowBalloonTip(
                timeout: 5000,
                tipTitle: "Now Playing Overlay Needs Attention",
                tipText: "The local host reported a fault. Open the logs for details.",
                tipIcon: ToolTipIcon.Error);
        }
        else if (!status.IsFaulted)
        {
            _faultNotificationShown = false;
        }
    }

    private void CopyOverlayUrl()
    {
        RunUserAction(
            "copy the OBS URL",
            () =>
            {
                Clipboard.SetDataObject(
                    _controller.OverlayUrl,
                    copy: true,
                    retryTimes: 10,
                    retryDelay: 100);
                _logger.LogInformation("Copied the OBS URL to the clipboard.");
            });
    }

    private void OpenOverlayPreview(OverlayPreviewOption option)
    {
        RunUserAction(
            $"open the {option.MenuText} overlay preview",
            () => OpenWithShell(_controller.BuildOverlayPreviewUrl(option.Scale)));
    }

    private void OpenLogs()
    {
        RunUserAction(
            "open the log directory",
            () =>
            {
                Directory.CreateDirectory(_controller.LogDirectory);
                OpenWithShell(_controller.LogDirectory);
            });
    }

    private async void ConfigurePort()
    {
        using var dialog = new PortConfigurationDialog(_controller.EffectivePort);
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var selectedPort = dialog.SelectedPort;
        await RunUserActionAsync(
            "change the port",
            async () =>
            {
                var result = await _controller.SavePortAsync(selectedPort);
                if (!result.Changed)
                {
                    MessageBox.Show(
                        "The selected port is already in use by this instance.",
                        "Now Playing Overlay",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                MessageBox.Show(
                    $"The server moved to port {selectedPort} without restarting. Loaded overlay pages were asked to follow the new URL:\n\n{result.OverlayUrl}\n\nUpdate the saved OBS Browser Source URL so future reloads and OBS restarts use the new port.",
                    "Port Changed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
    }

    private void RequestExit()
    {
        _logger.LogInformation("Exit requested from the tray menu.");
        ExitThread();
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs eventArgs)
    {
        _logger.LogInformation("Windows session ending; requesting graceful shutdown.");
        Application.Exit();
    }

    private void DisposeResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        _menu.Dispose();
    }

    private void RunUserAction(string description, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or Win32Exception
            or ExternalException
            or InvalidOperationException)
        {
            _logger.LogError(error, "Could not {ActionDescription}.", description);
            MessageBox.Show(
                $"Could not {description}. Open the log directory for details.",
                "Now Playing Overlay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task RunUserActionAsync(string description, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or Win32Exception
            or ExternalException
            or InvalidOperationException
            or HttpListenerException)
        {
            _logger.LogError(error, "Could not {ActionDescription}.", description);
            MessageBox.Show(
                $"Could not {description}. The current port remains active. Open the log directory for details.",
                "Now Playing Overlay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void OpenWithShell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
