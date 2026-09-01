using System.Text.Json;
using System.Text.Json.Serialization;

namespace NowPlayingOverlay.SessionProbe;

/// <summary>
/// Serializes the probe's intentionally privacy-sensitive JSONL records.
/// </summary>
internal sealed class ProbeLogSink : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StreamWriter? _file;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _sequence;

    private ProbeLogSink(StreamWriter? file)
    {
        _file = file;
    }

    public static async Task<ProbeLogSink> CreateAsync(string? outputPath)
    {
        if (outputPath is null)
        {
            return new ProbeLogSink(null);
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var writer = new StreamWriter(stream) { AutoFlush = true };
        await writer.WriteLineAsync(
            "# Local diagnostic output. Media text may contain private listening information.");
        return new ProbeLogSink(writer);
    }

    public async Task WriteAsync(string eventName, string? sourceAppUserModelId = null, object? details = null)
    {
        var record = new
        {
            // Keep sequence values unique across concurrent callbacks.
            Sequence = Interlocked.Increment(ref _sequence),
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = eventName,
            SourceAppUserModelId = sourceAppUserModelId,
            Details = details,
        };
        var json = JsonSerializer.Serialize(record, JsonOptions);

        // Serialize console and file writes to prevent interleaved JSONL records.
        await _writeLock.WaitAsync();
        try
        {
            Console.WriteLine(json);
            if (_file is not null)
            {
                await _file.WriteLineAsync(json);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_file is not null)
        {
            await _file.DisposeAsync();
        }

        _writeLock.Dispose();
    }
}
