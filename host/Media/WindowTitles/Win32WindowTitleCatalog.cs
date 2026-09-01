using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Media.WindowTitles;

/// <summary>
/// Enumerates visible windows into stable targets without retaining window handles.
/// </summary>
internal sealed class Win32WindowTitleCatalog : IWindowTitleCatalog
{
    private const int MaximumCaptionLength = 32767;
    private const int MaximumClassNameLength = 512;

    public IReadOnlyList<WindowTitleWindow> GetWindows()
    {
        var windows = new List<WindowTitleWindow>();
        var currentProcessId = Environment.ProcessId;
        EnumWindowsProc callback = (window, _) =>
        {
            TryAddWindow(window, currentProcessId, windows);
            return true;
        };
        if (!EnumWindows(callback, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate desktop windows.");
        }

        return windows.AsReadOnly();
    }

    private static void TryAddWindow(
        nint window,
        int currentProcessId,
        ICollection<WindowTitleWindow> windows)
    {
        if (!IsWindowVisible(window))
        {
            return;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == currentProcessId)
        {
            return;
        }

        var title = ReadWindowTitle(window);
        var windowClass = ReadWindowClass(window);
        if (string.IsNullOrWhiteSpace(title) || windowClass.Length == 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
            {
                return;
            }

            var target = new WindowTitleTargetSettings
            {
                ProcessName = processName,
                ExecutablePath = TryGetExecutablePath(process),
                WindowClass = windowClass,
            };
            target.Validate();
            windows.Add(new WindowTitleWindow(target, title));
        }
        catch (Exception error) when (error is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or Win32Exception
            or InvalidDataException)
        {
            // Windows can disappear between enumeration and process inspection.
        }
    }

    private static string ReadWindowTitle(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var capacity = Math.Min(length + 1, MaximumCaptionLength + 1);
        var buffer = new StringBuilder(capacity);
        var copied = GetWindowTextW(window, buffer, capacity);
        return copied <= 0 ? string.Empty : buffer.ToString();
    }

    private static string ReadWindowClass(nint window)
    {
        var buffer = new StringBuilder(MaximumClassNameLength + 1);
        var copied = GetClassNameW(window, buffer, buffer.Capacity);
        return copied <= 0 ? string.Empty : buffer.ToString();
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception error) when (error is InvalidOperationException
            or NotSupportedException
            or Win32Exception)
        {
            return null;
        }
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);
}
