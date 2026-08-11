using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.AspNetCore.Connections;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host;

public class Program
{
    private const string ApplicationTitle = "Now Playing Overlay";

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
        var settingsStore = new ApplicationSettingsStore(paths.SettingsFilePath);
        var loadedSettings = settingsStore.Load();
        if (loadedSettings.Warning is not null)
        {
            logFile.Write(LogLevel.Warning, "Bootstrap", default, loadedSettings.Warning);
        }

        WebApplication? app = null;
        NowPlayingOverlay.Host.Configuration.HostOptions? options = null;
        try
        {
            app = OverlayApplication.Build(args, loadedSettings.Settings.Port, logFile);
            options = app.Services.GetRequiredService<NowPlayingOverlay.Host.Configuration.HostOptions>();
            app.StartAsync().GetAwaiter().GetResult();

            var controller = new TrayMenuController(
                options,
                settingsStore,
                app.Services.GetRequiredService<TrayStatusService>(),
                paths.LogDirectory);
            var logger = app.Services.GetRequiredService<ILogger<TrayApplicationContext>>();
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

    private static void StopAndDispose(WebApplication? app, BoundedLogFile logFile)
    {
        if (app is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            app.StopAsync(timeout.Token).GetAwaiter().GetResult();
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
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
                settingsStore.Save(new ApplicationSettings { Port = selectedPort });
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

    private static bool IsAddressInUse(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is AddressInUseException
                || current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
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
