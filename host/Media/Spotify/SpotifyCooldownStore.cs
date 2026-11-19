using System.Text.Json;

namespace NowPlayingOverlay.Host.Media.Spotify;

/// <summary>Stores only hashed connection identities and cooldown metadata using atomic replacement.</summary>
internal sealed class SpotifyCooldownStore(string path)
{
    private const int MaximumBytes = 64 * 1024;
    private const int MaximumEntries = 100;
    private readonly string _path = Path.GetFullPath(path);

    public IReadOnlyList<SpotifyCooldown> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        using var stream = File.OpenRead(_path);
        var bytes = new byte[MaximumBytes + 1];
        var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > MaximumBytes)
        {
            throw new InvalidDataException("Spotify cooldown file exceeds the size limit.");
        }

        var document = JsonSerializer.Deserialize<StoreDocument>(bytes.AsSpan(0, count));
        if (document is not { Version: 1, Entries: { Length: <= MaximumEntries } entries })
        {
            throw new InvalidDataException("Invalid Spotify cooldown document.");
        }

        var keys = new HashSet<(string, SpotifyEndpoint)>();
        foreach (var entry in entries)
        {
            if (entry is null
                || entry.ClientKey is not { Length: 64 }
                || !entry.ClientKey.All(char.IsAsciiHexDigit)
                || !Enum.IsDefined(entry.Endpoint)
                || entry.Consecutive429 <= 0
                || entry.FirstFailureAtUtc > entry.NotBeforeUtc
                || !keys.Add((entry.ClientKey, entry.Endpoint)))
            {
                throw new InvalidDataException("Invalid Spotify cooldown entry.");
            }
        }

        return entries;
    }

    public void Save(IEnumerable<SpotifyCooldown> entries)
    {
        var document = new StoreDocument(1, entries.ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        if (document.Entries.Length > MaximumEntries || bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("Spotify cooldown storage limit exceeded.");
        }

        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".spotify-cooldowns-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record StoreDocument(int Version, SpotifyCooldown[] Entries);
}
