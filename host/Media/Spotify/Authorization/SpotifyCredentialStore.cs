using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal sealed class SpotifyCredentialStore
{
    private const int CurrentFormatVersion = 1;

    private static readonly byte[] OptionalEntropy =
        "NowPlayingOverlay.Spotify.RefreshToken.v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();
    private readonly string _filePath;

    public SpotifyCredentialStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public SpotifyStoredCredential? Load()
    {
        lock (_gate)
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
                var document = JsonSerializer.Deserialize<CredentialDocument>(plaintext, JsonOptions)
                    ?? throw new InvalidDataException("The Spotify credential file is empty.");
                if (document.FormatVersion != CurrentFormatVersion)
                {
                    throw new InvalidDataException("The Spotify credential format is unsupported.");
                }

                var credential = new SpotifyStoredCredential(
                    new SpotifyClientId(document.ClientId),
                    document.RefreshToken,
                    document.Scope);
                credential.Validate();
                return credential;
            }
            catch (Exception error) when (error is CryptographicException
                or JsonException
                or InvalidDataException
                or ArgumentException)
            {
                throw new InvalidDataException(
                    "The stored Spotify credential could not be read.",
                    error);
            }
            finally
            {
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }
    }

    public void Save(SpotifyStoredCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        credential.Validate();
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            byte[]? plaintext = null;
            byte[]? protectedBytes = null;
            try
            {
                plaintext = JsonSerializer.SerializeToUtf8Bytes(
                    new CredentialDocument
                    {
                        FormatVersion = CurrentFormatVersion,
                        ClientId = credential.ClientId.Value,
                        RefreshToken = credential.RefreshToken,
                        Scope = credential.Scope,
                    },
                    JsonOptions);
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
    }

    public void Delete()
    {
        lock (_gate)
        {
            File.Delete(_filePath);
            TryDelete(_filePath + ".tmp");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Preserve the primary persistence result over best-effort temporary cleanup.
        }
    }

    private sealed record CredentialDocument
    {
        public int FormatVersion { get; init; }

        public required string ClientId { get; init; }

        public required string RefreshToken { get; init; }

        public required string Scope { get; init; }
    }
}
