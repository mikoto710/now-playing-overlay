using System.Security.Cryptography;
using NowPlayingOverlay.Host.Media;

namespace NowPlayingOverlay.Host.State;

internal sealed class ArtworkCache
{
    private readonly object _gate = new();
    private readonly ArtworkCacheOptions _options;
    private readonly Dictionary<string, CacheItem> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _protectedIds = new(StringComparer.Ordinal);
    private long _accessSequence;
    private long _totalBytes;

    public ArtworkCache(ArtworkCacheOptions? options = null)
    {
        _options = options ?? new ArtworkCacheOptions();
        _options.Validate();
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public bool TryAdd(ArtworkPayload payload, out ArtworkCacheEntry? entry, out bool added)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var bytes = payload.Bytes;
        if (bytes.Length > _options.MaximumItemBytes
            || !ArtworkImageInspector.TryInspect(bytes.Span, out var contentType, out var width, out var height)
            || width > _options.MaximumWidth
            || height > _options.MaximumHeight
            || (long)width * height > _options.MaximumPixels)
        {
            entry = null;
            added = false;
            return false;
        }

        var artworkId = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        lock (_gate)
        {
            if (_entries.TryGetValue(artworkId, out var existing))
            {
                existing.LastAccess = NextAccessSequence();
                entry = existing.Entry;
                added = false;
                return true;
            }

            var requiredEntries = _entries.Count + 1 - _options.MaximumEntries;
            var requiredBytes = _totalBytes + bytes.Length - _options.MaximumTotalBytes;
            // Plan every eviction before mutation so protected entries stay available.
            var evictions = SelectEvictions(requiredEntries, requiredBytes);
            if (evictions is null)
            {
                entry = null;
                added = false;
                return false;
            }

            foreach (var eviction in evictions)
            {
                _entries.Remove(eviction.Entry.ArtworkId);
                _totalBytes -= eviction.Entry.ByteLength;
            }

            entry = new ArtworkCacheEntry(
                artworkId,
                contentType,
                bytes.ToArray(),
                width,
                height);
            _entries.Add(artworkId, new CacheItem(entry, NextAccessSequence()));
            _totalBytes += entry.ByteLength;
            added = true;
            return true;
        }
    }

    public bool TryGet(string artworkId, out ArtworkCacheEntry? entry)
    {
        if (string.IsNullOrEmpty(artworkId))
        {
            entry = null;
            return false;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(artworkId, out var item))
            {
                entry = null;
                return false;
            }

            item.LastAccess = NextAccessSequence();
            entry = item.Entry;
            return true;
        }
    }

    public void SetProtectedIds(params string?[] artworkIds)
    {
        ArgumentNullException.ThrowIfNull(artworkIds);
        lock (_gate)
        {
            _protectedIds.Clear();
            foreach (var artworkId in artworkIds)
            {
                if (artworkId is not null && _entries.ContainsKey(artworkId))
                {
                    _protectedIds.Add(artworkId);
                }
            }
        }
    }

    private List<CacheItem>? SelectEvictions(int requiredEntries, long requiredBytes)
    {
        if (requiredEntries <= 0 && requiredBytes <= 0)
        {
            return [];
        }

        var selected = new List<CacheItem>();
        long releasedBytes = 0;
        foreach (var candidate in _entries.Values
                     .Where(item => !_protectedIds.Contains(item.Entry.ArtworkId))
                     .OrderBy(item => item.LastAccess))
        {
            selected.Add(candidate);
            releasedBytes += candidate.Entry.ByteLength;
            if (selected.Count >= requiredEntries && releasedBytes >= requiredBytes)
            {
                return selected;
            }
        }

        return null;
    }

    private long NextAccessSequence()
    {
        return checked(++_accessSequence);
    }

    private sealed class CacheItem(ArtworkCacheEntry entry, long lastAccess)
    {
        public ArtworkCacheEntry Entry { get; } = entry;

        public long LastAccess { get; set; } = lastAccess;
    }
}
