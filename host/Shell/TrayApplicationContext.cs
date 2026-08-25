using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayMenuController _controller;
    private readonly ClipboardTextWriter _clipboard = new();
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _applicationIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private bool _faultNotificationShown;
    private bool _settingsOperationActive;
    private bool _disposed;

    public TrayApplicationContext(
        TrayMenuController controller,
        ILogger<TrayApplicationContext> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _settingsItem = CreateMenuItem("Settings...", ConfigureSettings);
        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            CreateMenuItem("Copy OBS URL", CopyOverlayUrl),
            CreateOverlayPreviewMenu(),
            CreateMenuItem("Open Logs", OpenLogs),
            _settingsItem,
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

        var dropDown = (ToolStripDropDownMenu)item.DropDown;
        dropDown.ShowImageMargin = false;
        dropDown.ShowCheckMargin = false;
        item.DropDownOpening += (_, _) =>
        {
            // Nested WinForms drop-downs can calculate a taller preferred item size at high DPI.
            // The visible parent item is already scaled correctly, so use its actual height.
            foreach (ToolStripItem previewItem in item.DropDownItems)
            {
                previewItem.AutoSize = false;
                previewItem.Height = item.Height;
            }
        };
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
                _clipboard.SetText(_controller.OverlayUrl);
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

    private async void ConfigureSettings()
    {
        if (_settingsOperationActive)
        {
            return;
        }

        _settingsOperationActive = true;
        _settingsItem.Enabled = false;
        try
        {
            await RunUserActionAsync(
                "save settings",
                async () =>
                {
                    var discovery = await _controller.RefreshSourcesAsync(
                        SourceProvider.WindowsMedia);
                    var settings = _controller.GetSettings();
                    using var dialog = new SettingsDialog(
                        _controller.EffectivePort,
                        discovery,
                        settings.Source,
                        settings.WindowsMedia,
                        _controller.GetSpotifyConnection(),
                        settings.Appearance,
                        _controller.RefreshSourcesAsync,
                        _controller.AuthorizeSpotifyAsync,
                        _controller.DisconnectSpotifyAsync);
                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }

                    var result = await _controller.SaveSettingsAsync(
                        dialog.SelectedPort,
                        dialog.SelectedProvider,
                        dialog.SelectedInstanceId,
                        dialog.SelectedAppearance);
                    if (result.PortChanged)
                    {
                        MessageBox.Show(
                            $"Settings were saved and the server moved to port {dialog.SelectedPort} without restarting. Loaded overlay pages were asked to follow the new URL:\n\n{result.OverlayUrl}\n\nUpdate the saved OBS Browser Source URL so future reloads and OBS restarts use the new port.",
                            "Settings Saved",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                });
        }
        finally
        {
            _settingsOperationActive = false;
            if (!_disposed)
            {
                _settingsItem.Enabled = true;
            }
        }
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
