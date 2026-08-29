using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host;

internal static class Program
{
    private const string ApplicationTitle = "Now Playing Overlay";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationPaths paths;
        BoundedLogFile logFile;
        try
        {
            paths = ApplicationPaths.ForCurrentUser();
            logFile = new BoundedLogFile(paths.LogFilePath);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            ShowMessage(
                "The local application data directory could not be initialized. The app cannot start.",
                MessageBoxIcon.Error);
            return 1;
        }

        using (logFile)
        {
            RegisterUnhandledExceptionLogging(logFile);
            logFile.Write(LogLevel.Information, "Bootstrap", default, "Starting Now Playing Overlay.");
            if (!SingleInstanceGuard.TryAcquire(
                    SingleInstanceGuard.ApplicationMutexName,
                    out var singleInstance))
            {
                logFile.Write(
                    LogLevel.Warning,
                    "Bootstrap",
                    default,
                    "A second application instance was rejected.");
                ShowMessage("Now Playing Overlay is already running.", MessageBoxIcon.Information);
                return 2;
            }

            using (singleInstance)
            {
                return RunApplication(args, paths, logFile);
            }
        }
    }

    private static int RunApplication(
        string[] args,
        ApplicationPaths paths,
        BoundedLogFile logFile)
    {
        var settingsStore = new ApplicationSettingsStore(
            paths.SettingsFilePath,
            paths.RootDirectory);
        var loadedSettings = settingsStore.Load();
        if (loadedSettings.Warning is not null)
        {
            logFile.Write(LogLevel.Warning, "Bootstrap", default, loadedSettings.Warning);
        }

        OverlayApplication? app = null;
        HostOptions? options = null;
        try
        {
            app = OverlayApplication.Build(args, loadedSettings.Settings, paths, logFile);
            options = app.Options;
            app.StartAsync().GetAwaiter().GetResult();

            var controller = new TrayMenuController(
                () => app.CurrentPort,
                settingsStore,
                app.StatusService,
                paths.LogDirectory,
                (port, persistPort, cancellationToken) =>
                    app.RebindPortAsync(port, persistPort, cancellationToken),
                app.GetSourceState,
                app.RefreshSourcesAsync,
                app.SelectSource,
                app.GetSpotifyConnectionState,
                (clientId, reauthorize, cancellationToken) => reauthorize
                    ? app.ReauthorizeSpotifyAsync(clientId, cancellationToken)
                    : app.ConnectSpotifyAsync(clientId, cancellationToken),
                app.DisconnectSpotifyAsync,
                app.ExportIngestKey,
                app.RotateIngestKey,
                app.SetAppearance,
                app.GetOutputStatus,
                app.RenderOutputPreview,
                app.SetOutputs);
            var logger = new BoundedFileLoggerProvider(logFile).CreateLogger<TrayApplicationContext>();
            using var tray = new TrayApplicationContext(controller, logger);
            Application.Run(tray);
            return 0;
        }
        catch (Exception error)
        {
            logFile.Write(
                LogLevel.Critical,
                "Bootstrap",
                default,
                "The application could not start or continue running.",
                error);
            StopAndDispose(app, logFile);
            app = null;
            ShowStartupFailure(
                error,
                options?.Port ?? loadedSettings.Settings.Port,
                paths.LogDirectory,
                settingsStore,
                logFile);
            return 1;
        }
        finally
        {
            StopAndDispose(app, logFile);
        }
    }

    private static void StopAndDispose(OverlayApplication? app, BoundedLogFile logFile)
    {
        if (app is null)
        {
            return;
        }

        // The WinForms message pump is no longer available during cleanup.
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            using var timeout = new CancellationTokenSource(ShutdownTimeout);
            app.StopAsync(timeout.Token)
                .WaitAsync(timeout.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception error)
        {
            logFile.Write(
                LogLevel.Error,
                "Bootstrap",
                default,
                "The application did not stop cleanly.",
                error);
        }

        try
        {
            using var timeout = new CancellationTokenSource(ShutdownTimeout);
            app.DisposeAsync()
                .AsTask()
                .WaitAsync(timeout.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception error)
        {
            logFile.Write(
                LogLevel.Error,
                "Bootstrap",
                default,
                "The application host could not be disposed cleanly.",
                error);
        }

        logFile.Write(LogLevel.Information, "Bootstrap", default, "Now Playing Overlay stopped.");
    }

    private static void RegisterUnhandledExceptionLogging(BoundedLogFile logFile)
    {
        Application.ThreadException += (_, eventArgs) =>
        {
            logFile.Write(
                LogLevel.Critical,
                "Unhandled",
                default,
                "An unhandled tray thread exception occurred.",
                eventArgs.Exception);
            ShowMessage("An unexpected error occurred. See the logs for details.", MessageBoxIcon.Error);
            Application.ExitThread();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            logFile.Write(
                LogLevel.Critical,
                "Unhandled",
                default,
                "An unhandled application exception occurred.",
                eventArgs.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            logFile.Write(
                LogLevel.Error,
                "Unhandled",
                default,
                "An unobserved background task exception occurred.",
                eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private static void ShowStartupFailure(
        Exception error,
        int port,
        string logDirectory,
        ApplicationSettingsStore settingsStore,
        BoundedLogFile logFile)
    {
        if (!IsAddressInUse(error))
        {
            ShowMessage($"Now Playing Overlay could not start.\n\nLogs: {logDirectory}", MessageBoxIcon.Error);
            return;
        }

        var configure = MessageBox.Show(
            $"Port {port} is unavailable. Would you like to save a different loopback port?\n\nLogs: {logDirectory}",
            ApplicationTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        if (configure == DialogResult.Yes)
        {
            PromptForAlternativePort(port, settingsStore, logFile);
        }
    }

    private static void PromptForAlternativePort(
        int currentPort,
        ApplicationSettingsStore settingsStore,
        BoundedLogFile logFile)
    {
        while (true)
        {
            using var dialog = new PortConfigurationDialog(currentPort);
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var selectedPort = dialog.SelectedPort;
            if (!LoopbackPortProbe.IsAvailable(selectedPort))
            {
                ShowMessage(
                    $"Port {selectedPort} is also unavailable. Choose another port.",
                    MessageBoxIcon.Warning);
                currentPort = selectedPort;
                continue;
            }

            try
            {
                settingsStore.Update(current => current with { Port = selectedPort });
                var overlayUrl = TrayMenuController.BuildOverlayUrl(selectedPort);
                ShowMessage(
                    $"Port {selectedPort} was saved. Restart Now Playing Overlay, then update the OBS Browser Source URL to:\n\n{overlayUrl}",
                    MessageBoxIcon.Information);
                return;
            }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                logFile.Write(
                    LogLevel.Error,
                    "Bootstrap",
                    default,
                    "The replacement port could not be saved.",
                    saveError);
                ShowMessage("The port could not be saved. See the logs for details.", MessageBoxIcon.Error);
                return;
            }
        }
    }

    internal static bool IsAddressInUse(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                || current is HttpListenerException { NativeErrorCode: 32 or 183 or 10048 })
            {
                return true;
            }
        }

        return false;
    }

    private static void ShowMessage(string message, MessageBoxIcon icon)
    {
        MessageBox.Show(
            message,
            ApplicationTitle,
            MessageBoxButtons.OK,
            icon);
    }
}
