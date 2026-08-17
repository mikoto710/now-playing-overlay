using System.Net;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal readonly record struct SpotifyClientId
{
    public SpotifyClientId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (value.Length > 256 || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException(
                "Spotify Client ID must be at most 256 non-whitespace characters.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

internal sealed record SpotifyAccessToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    public bool IsUsableAt(DateTimeOffset now, TimeSpan refreshSkew)
    {
        return ExpiresAtUtc > now.Add(refreshSkew);
    }
}

internal sealed record SpotifyTokenResult(
    SpotifyAccessToken AccessToken,
    string? RefreshToken,
    string? Scope);

internal sealed record SpotifyStoredCredential(
    SpotifyClientId ClientId,
    string RefreshToken,
    string Scope)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RefreshToken)
            || RefreshToken.Length > 8192
            || RefreshToken.Any(char.IsControl))
        {
            throw new InvalidDataException("The stored Spotify refresh token is invalid.");
        }

        if (string.IsNullOrWhiteSpace(Scope)
            || Scope.Length > 1024
            || Scope.Any(char.IsControl)
            || !SpotifyAuthorizationRequest.HasRequiredScope(Scope))
        {
            throw new InvalidDataException("The stored Spotify credential lacks the required scope.");
        }
    }
}

internal enum SpotifyConnectionStatus
{
    Disconnected,
    Connected,
    ClientIdMismatch,
    CredentialUnavailable,
}

internal sealed record SpotifyConnectionState(
    SpotifyConnectionStatus Status,
    SpotifyClientId? ConnectedClientId = null);

internal sealed class SpotifyAuthorizationException : Exception
{
    public SpotifyAuthorizationException(string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}

internal sealed class SpotifyTokenRequestException : Exception
{
    public SpotifyTokenRequestException(
        HttpStatusCode statusCode,
        string? errorCode,
        string message,
        TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ErrorCode { get; }

    public TimeSpan? RetryAfter { get; }

    public bool RequiresReauthorization =>
        string.Equals(ErrorCode, "invalid_grant", StringComparison.Ordinal);
}

internal sealed class SpotifyReauthorizationRequiredException : Exception
{
    public SpotifyReauthorizationRequiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
