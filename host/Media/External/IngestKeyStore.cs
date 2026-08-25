using System.Security.Cryptography;

namespace NowPlayingOverlay.Host.Media.External;

internal sealed class IngestKey : IDisposable
{
    public const int ByteLength = 32;
    public const int EncodedLength = 43;
    private byte[]? _bytes;

    private IngestKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException($"Ingest key must contain exactly {ByteLength} bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public static IngestKey Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        try
        {
            return new IngestKey(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Export()
    {
        var bytes = GetBytes();
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public bool MatchesAuthorization(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null
            || authorization.Length != prefix.Length + EncodedLength
            || !authorization.AsSpan(0, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Span<char> encoded = stackalloc char[44];
        var token = authorization.AsSpan(prefix.Length);
        for (var index = 0; index < token.Length; index++)
        {
            encoded[index] = token[index] switch
            {
                '-' => '+',
                '_' => '/',
                var character => character,
            };
        }

        encoded[^1] = '=';
        Span<byte> candidate = stackalloc byte[ByteLength];
        try
        {
            return Convert.TryFromBase64Chars(encoded, candidate, out var written)
                && written == ByteLength
                && CryptographicOperations.FixedTimeEquals(GetBytes(), candidate);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static IngestKey FromBytes(ReadOnlySpan<byte> bytes)
    {
        return new IngestKey(bytes);
    }

    internal void CopyTo(Span<byte> destination)
    {
        GetBytes().CopyTo(destination);
    }

    private byte[] GetBytes()
    {
        return _bytes ?? throw new ObjectDisposedException(nameof(IngestKey));
    }
}

internal sealed class IngestKeyStore
{
    private const byte CurrentFormatVersion = 1;
    private const int HeaderLength = 5;
    private static readonly byte[] Magic = "NPIK"u8.ToArray();
    private static readonly byte[] OptionalEntropy =
        "NowPlayingOverlay.ExternalPush.IngestKey.v1"u8.ToArray();
    private readonly object _gate = new();
    private readonly string _filePath;

    public IngestKeyStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public IngestKey LoadOrCreate()
    {
        lock (_gate)
        {
            var existing = LoadCore();
            if (existing is not null)
            {
                return existing;
            }

            var created = IngestKey.Generate();
            try
            {
                SaveCore(created);
                return created;
            }
            catch
            {
                created.Dispose();
                throw;
            }
        }
    }

    public IngestKey Rotate()
    {
        lock (_gate)
        {
            var replacement = IngestKey.Generate();
            try
            {
                SaveCore(replacement);
                return replacement;
            }
            catch
            {
                replacement.Dispose();
                throw;
            }
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            File.Delete(_filePath);
            TryDelete(_filePath + ".tmp");
        }
    }

    private IngestKey? LoadCore()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        byte[]? plaintext = null;
        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            plaintext = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            if (plaintext.Length != HeaderLength + IngestKey.ByteLength
                || !plaintext.AsSpan(0, Magic.Length).SequenceEqual(Magic)
                || plaintext[Magic.Length] != CurrentFormatVersion)
            {
                throw new InvalidDataException("The ingest key format is unsupported.");
            }

            return IngestKey.FromBytes(plaintext.AsSpan(HeaderLength, IngestKey.ByteLength));
        }
        catch (Exception error) when (error is CryptographicException or InvalidDataException)
        {
            throw new InvalidDataException("The stored ingest key could not be read.", error);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private void SaveCore(IngestKey key)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        byte[]? plaintext = null;
        byte[]? protectedBytes = null;
        try
        {
            plaintext = new byte[HeaderLength + IngestKey.ByteLength];
            Magic.CopyTo(plaintext, 0);
            plaintext[Magic.Length] = CurrentFormatVersion;
            key.CopyTo(plaintext.AsSpan(HeaderLength));
            protectedBytes = ProtectedData.Protect(
                plaintext,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
