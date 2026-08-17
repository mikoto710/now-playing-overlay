using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal sealed class SpotifyTokenClient
{
    private const int MaximumResponseBytes = 64 * 1024;

    private static readonly Uri TokenEndpoint = new("https://accounts.spotify.com/api/token");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public SpotifyTokenClient(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<SpotifyTokenResult> ExchangeAuthorizationCodeAsync(
        SpotifyClientId clientId,
        string authorizationCode,
        Uri redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        return SendAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId.Value,
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["redirect_uri"] = redirectUri.AbsoluteUri,
                ["code_verifier"] = codeVerifier,
            },
            requireRefreshToken: true,
            requireScope: true,
            cancellationToken);
    }

    public Task<SpotifyTokenResult> RefreshAsync(
        SpotifyClientId clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return SendAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId.Value,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            requireRefreshToken: false,
            requireScope: false,
            cancellationToken);
    }

    private async Task<SpotifyTokenResult> SendAsync(
        IReadOnlyDictionary<string, string> parameters,
        bool requireRefreshToken,
        bool requireScope,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters),
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new SpotifyTokenRequestException(
                response.StatusCode,
                errorCode: null,
                "Spotify token response exceeded the supported size.");
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (body.Length > MaximumResponseBytes)
        {
            throw new SpotifyTokenRequestException(
                response.StatusCode,
                errorCode: null,
                "Spotify token response exceeded the supported size.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = TryReadErrorCode(body);
            throw new SpotifyTokenRequestException(
                response.StatusCode,
                errorCode,
                errorCode is null
                    ? $"Spotify token request failed with HTTP {(int)response.StatusCode}."
                    : $"Spotify token request failed with '{errorCode}'.",
                GetRetryAfter(response));
        }

        TokenResponse document;
        try
        {
            document = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                ?? throw new JsonException("Spotify token response was empty.");
        }
        catch (JsonException error)
        {
            throw new SpotifyTokenRequestException(
                response.StatusCode,
                errorCode: null,
                $"Spotify token response was invalid: {error.Message}");
        }

        ValidateToken(document.AccessToken, "access token", 16384, required: true);
        ValidateToken(document.RefreshToken, "refresh token", 8192, requireRefreshToken);
        if (!string.Equals(document.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidSuccessResponse(response.StatusCode, "token type");
        }

        if (document.ExpiresIn <= 0)
        {
            throw InvalidSuccessResponse(response.StatusCode, "expiry");
        }

        if (document.Scope is { Length: > 1024 }
            || document.Scope?.Any(char.IsControl) == true
            || requireScope && !SpotifyAuthorizationRequest.HasRequiredScope(document.Scope))
        {
            throw InvalidSuccessResponse(response.StatusCode, "scope");
        }

        DateTimeOffset expiresAt;
        try
        {
            expiresAt = _timeProvider.GetUtcNow().AddSeconds(document.ExpiresIn);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidSuccessResponse(response.StatusCode, "expiry");
        }

        return new SpotifyTokenResult(
            new SpotifyAccessToken(document.AccessToken!, expiresAt),
            document.RefreshToken,
            document.Scope);
    }

    private static string? TryReadErrorCode(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                var value = error.GetString();
                return value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl)
                    ? value
                    : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - _timeProvider.GetUtcNow();
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private static void ValidateToken(
        string? value,
        string name,
        int maximumLength,
        bool required)
    {
        if (value is null && !required)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw InvalidSuccessResponse(HttpStatusCode.OK, name);
        }
    }

    private static SpotifyTokenRequestException InvalidSuccessResponse(
        HttpStatusCode statusCode,
        string field)
    {
        return new SpotifyTokenRequestException(
            statusCode,
            errorCode: null,
            $"Spotify token response contained an invalid {field}.");
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }
    }
}
