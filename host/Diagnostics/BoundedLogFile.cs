using System.Text;

namespace NowPlayingOverlay.Host.Diagnostics;

internal sealed class BoundedLogFile : IDisposable
{
    public const long DefaultMaximumFileBytes = 1024 * 1024;
    public const int DefaultMaximumFileCount = 5;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly long _maximumFileBytes;
    private readonly int _maximumFileCount;
    private FileStream? _stream;
    private bool _disabled;
    private bool _disposed;

    public BoundedLogFile(
        string filePath,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int maximumFileCount = DefaultMaximumFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (maximumFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        if (maximumFileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileCount));
        }

        _filePath = Path.GetFullPath(filePath);
        _maximumFileBytes = maximumFileBytes;
        _maximumFileCount = maximumFileCount;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        DeleteFilesBeyondRetention();
        OpenCurrentFile();
        if (_stream!.Length > _maximumFileBytes)
        {
            Rotate();
        }
    }

    public void Write(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_disabled)
            {
                return;
            }

            try
            {
                var bytes = EncodeBoundedEntry(level, category, eventId, message, exception);
                if (_stream!.Length > 0 && _stream.Length + bytes.Length > _maximumFileBytes)
                {
                    Rotate();
                }

                _stream!.Write(bytes);
                _stream.Flush(flushToDisk: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Logging must not take down the overlay if the local disk becomes unavailable.
                _disabled = true;
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream?.Dispose();
            _stream = null;
        }
    }

    private byte[] EncodeBoundedEntry(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var entry = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(' ')
            .Append(level.ToString().ToUpperInvariant())
            .Append(' ')
            .Append(category);
        if (eventId.Id != 0)
        {
            entry.Append('[').Append(eventId.Id).Append(']');
        }

        entry.Append(" - ").Append(message);
        if (exception is not null)
        {
            entry.AppendLine()
                .Append(exception.GetType().FullName)
                .Append(": ")
                .Append(exception.Message);
            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                entry.AppendLine().Append(exception.StackTrace);
            }
        }

        entry.AppendLine();
        var encoded = Utf8.GetBytes(entry.ToString());
        if (encoded.LongLength <= _maximumFileBytes)
        {
            return encoded;
        }

        const string suffix = "... log entry truncated\r\n";
        var suffixBytes = Utf8.GetBytes(suffix);
        var budget = checked((int)Math.Max(0, _maximumFileBytes - suffixBytes.Length));
        var truncated = new StringBuilder();
        var used = 0;
        foreach (var rune in entry.ToString().EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > budget)
            {
                break;
            }

            truncated.Append(rune);
            used += rune.Utf8SequenceLength;
        }

        truncated.Append(suffix);
        return Utf8.GetBytes(truncated.ToString());
    }

    private void Rotate()
    {
        _stream?.Dispose();
        _stream = null;
        if (_maximumFileCount == 1)
        {
            File.Delete(_filePath);
            OpenCurrentFile();
            return;
        }

        File.Delete(GetArchivePath(_maximumFileCount - 1));
        for (var index = _maximumFileCount - 2; index >= 1; index--)
        {
            var source = GetArchivePath(index);
            if (File.Exists(source))
            {
                File.Move(source, GetArchivePath(index + 1), overwrite: true);
            }
        }

        if (File.Exists(_filePath))
        {
            File.Move(_filePath, GetArchivePath(1), overwrite: true);
        }

        OpenCurrentFile();
    }

    private void OpenCurrentFile()
    {
        _stream = new FileStream(
            _filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
    }

    private string GetArchivePath(int index)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var name = Path.GetFileNameWithoutExtension(_filePath);
        var extension = Path.GetExtension(_filePath);
        return Path.Combine(directory, $"{name}.{index}{extension}");
    }

    private void DeleteFilesBeyondRetention()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var name = Path.GetFileNameWithoutExtension(_filePath);
        var extension = Path.GetExtension(_filePath);
        foreach (var path in Directory.EnumerateFiles(directory, $"{name}.*{extension}"))
        {
            var archiveName = Path.GetFileNameWithoutExtension(path);
            var prefix = name + ".";
            if (!archiveName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var archivePart = archiveName[prefix.Length..];
            if (int.TryParse(archivePart, out var index) && index >= _maximumFileCount)
            {
                File.Delete(path);
            }
        }
    }
}
