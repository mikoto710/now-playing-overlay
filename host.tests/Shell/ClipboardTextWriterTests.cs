using System.ComponentModel;
using System.Runtime.InteropServices;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class ClipboardTextWriterTests
{
    [Fact]
    public void WritesUnicodeTextAndTransfersMemoryOwnership()
    {
        using var native = new FakeClipboardNativeApi();
        var writer = new ClipboardTextWriter(native, _ => { });

        writer.SetText("http://127.0.0.1:13130/NowPlaying.html");

        Assert.Equal("http://127.0.0.1:13130/NowPlaying.html", native.CapturedText);
        Assert.Equal(1, native.OpenCalls);
        Assert.Equal(1, native.EmptyCalls);
        Assert.Equal(1, native.SetTextCalls);
        Assert.Equal(1, native.CloseCalls);
        Assert.Equal(0, native.FreeCalls);
        Assert.True(native.OwnershipTransferred);
    }

    [Fact]
    public void RetriesOnlyOpeningTheClipboardBeforeWriting()
    {
        using var native = new FakeClipboardNativeApi();
        native.OpenResults.Enqueue(false);
        native.OpenResults.Enqueue(false);
        native.OpenResults.Enqueue(true);
        var delays = new List<int>();
        var writer = new ClipboardTextWriter(native, delays.Add);

        writer.SetText("value");

        Assert.Equal(3, native.OpenCalls);
        Assert.Equal(
            [
                ClipboardTextWriter.OpenRetryDelayMilliseconds,
                ClipboardTextWriter.OpenRetryDelayMilliseconds,
            ],
            delays);
        Assert.Equal(1, native.SetTextCalls);
    }

    [Fact]
    public void FreesMemoryWhenOpeningTheClipboardNeverSucceeds()
    {
        using var native = new FakeClipboardNativeApi { DefaultOpenResult = false };
        var writer = new ClipboardTextWriter(native, _ => { });

        var error = Assert.Throws<Win32Exception>(() => writer.SetText("value"));

        Assert.Equal(native.ErrorCode, error.NativeErrorCode);
        Assert.Equal(ClipboardTextWriter.OpenAttemptCount, native.OpenCalls);
        Assert.Equal(0, native.CloseCalls);
        Assert.Equal(1, native.FreeCalls);
    }

    [Fact]
    public void ClosesClipboardAndFreesMemoryWhenSettingDataFails()
    {
        using var native = new FakeClipboardNativeApi { SetTextResult = false };
        var writer = new ClipboardTextWriter(native, _ => { });

        var error = Assert.Throws<Win32Exception>(() => writer.SetText("value"));

        Assert.Equal(native.ErrorCode, error.NativeErrorCode);
        Assert.Equal(1, native.CloseCalls);
        Assert.Equal(1, native.FreeCalls);
        Assert.False(native.OwnershipTransferred);
    }

    private sealed class FakeClipboardNativeApi : IClipboardNativeApi, IDisposable
    {
        private IntPtr _memory;

        public Queue<bool> OpenResults { get; } = new();

        public bool DefaultOpenResult { get; init; } = true;

        public bool SetTextResult { get; init; } = true;

        public int ErrorCode { get; } = 5;

        public int OpenCalls { get; private set; }

        public int EmptyCalls { get; private set; }

        public int SetTextCalls { get; private set; }

        public int CloseCalls { get; private set; }

        public int FreeCalls { get; private set; }

        public bool OwnershipTransferred { get; private set; }

        public string? CapturedText { get; private set; }

        public IntPtr Allocate(nuint bytes)
        {
            _memory = Marshal.AllocHGlobal(checked((nint)bytes));
            return _memory;
        }

        public IntPtr Lock(IntPtr memory) => memory;

        public void Unlock(IntPtr memory)
        {
        }

        public void Free(IntPtr memory)
        {
            FreeCalls++;
            FreeMemory();
        }

        public bool Open()
        {
            OpenCalls++;
            return OpenResults.Count > 0 ? OpenResults.Dequeue() : DefaultOpenResult;
        }

        public bool Empty()
        {
            EmptyCalls++;
            return true;
        }

        public bool SetUnicodeText(IntPtr memory)
        {
            SetTextCalls++;
            if (!SetTextResult)
            {
                return false;
            }

            CapturedText = Marshal.PtrToStringUni(memory);
            OwnershipTransferred = true;
            FreeMemory();
            return true;
        }

        public void Close()
        {
            CloseCalls++;
        }

        public int GetLastError() => ErrorCode;

        public void Dispose()
        {
            FreeMemory();
        }

        private void FreeMemory()
        {
            if (_memory == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeHGlobal(_memory);
            _memory = IntPtr.Zero;
        }
    }
}
