using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class ClipboardTextWriter
{
    internal const int OpenAttemptCount = 10;
    internal const int OpenRetryDelayMilliseconds = 100;

    private readonly IClipboardNativeApi _native;
    private readonly Action<int> _wait;

    public ClipboardTextWriter()
        : this(WindowsClipboardNativeApi.Instance, Thread.Sleep)
    {
    }

    internal ClipboardTextWriter(IClipboardNativeApi native, Action<int>? wait = null)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _wait = wait ?? Thread.Sleep;
    }

    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = _native.Allocate((nuint)bytes.Length);
        if (memory == IntPtr.Zero)
        {
            throw CreateError("allocate clipboard memory");
        }

        try
        {
            var target = _native.Lock(memory);
            if (target == IntPtr.Zero)
            {
                throw CreateError("lock clipboard memory");
            }

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                _native.Unlock(memory);
            }

            OpenClipboard();
            try
            {
                if (!_native.Empty())
                {
                    throw CreateError("empty the clipboard");
                }

                if (!_native.SetUnicodeText(memory))
                {
                    throw CreateError("write Unicode text to the clipboard");
                }

                // Windows owns the movable memory after SetClipboardData succeeds.
                memory = IntPtr.Zero;
            }
            finally
            {
                _native.Close();
            }
        }
        finally
        {
            if (memory != IntPtr.Zero)
            {
                _native.Free(memory);
            }
        }
    }

    private void OpenClipboard()
    {
        var errorCode = 0;
        for (var attempt = 1; attempt <= OpenAttemptCount; attempt++)
        {
            if (_native.Open())
            {
                return;
            }

            errorCode = _native.GetLastError();
            if (attempt < OpenAttemptCount)
            {
                _wait(OpenRetryDelayMilliseconds);
            }
        }

        throw new Win32Exception(errorCode, "Could not open the Windows clipboard.");
    }

    private Win32Exception CreateError(string action)
    {
        return new Win32Exception(_native.GetLastError(), $"Could not {action}.");
    }
}

internal interface IClipboardNativeApi
{
    IntPtr Allocate(nuint bytes);

    IntPtr Lock(IntPtr memory);

    void Unlock(IntPtr memory);

    void Free(IntPtr memory);

    bool Open();

    bool Empty();

    bool SetUnicodeText(IntPtr memory);

    void Close();

    int GetLastError();
}

internal sealed class WindowsClipboardNativeApi : IClipboardNativeApi
{
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    private const uint CfUnicodeText = 13;

    public static WindowsClipboardNativeApi Instance { get; } = new();

    private WindowsClipboardNativeApi()
    {
    }

    public IntPtr Allocate(nuint bytes) =>
        GlobalAlloc(GmemMoveable | GmemZeroInit, bytes);

    public IntPtr Lock(IntPtr memory) => GlobalLock(memory);

    public void Unlock(IntPtr memory) => _ = GlobalUnlock(memory);

    public void Free(IntPtr memory) => _ = GlobalFree(memory);

    public bool Open() => OpenClipboard(IntPtr.Zero);

    public bool Empty() => EmptyClipboard();

    public bool SetUnicodeText(IntPtr memory) =>
        SetClipboardData(CfUnicodeText, memory) != IntPtr.Zero;

    public void Close() => _ = CloseClipboard();

    public int GetLastError() => Marshal.GetLastWin32Error();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
}
