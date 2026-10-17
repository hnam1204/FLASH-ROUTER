using System.Net;
using System.Text.Json;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.OAuthCallback.Security;

namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public sealed class OpenAiHttpOAuthTokenClient : IOAuthTokenClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOAuthOptions _options;

    public OpenAiHttpOAuthTokenClient(HttpClient httpClient, OpenAiOAuthOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public Task<OAuthTokenSet> ExchangeCodeAsync(OAuthTokenExchangeRequest request, CancellationToken cancellationToken = default)
        => SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = request.ClientId,
                ["code"] = request.Code,
                ["code_verifier"] = request.CodeVerifier,
                ["redirect_uri"] = request.RedirectUri,
                ["resource"] = request.Resource
            },
            cancellationToken);

    public Task<OAuthTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
        => SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
                ["resource"] = OpenAiOAuthOptions.Resource
            },
            cancellationToken);

    public async Task<bool> RevokeAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var revocationEndpoint = await ResolveRevocationEndpointAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, revocationEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = clientId
            })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new OAuthTokenClientException("REVOCATION_UNCONFIRMED", "Remote token revocation could not be confirmed.");
        }

        return false;
    }

    private async Task<OAuthTokenSet> SendTokenRequestAsync(
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiOAuthOptions.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorCode = TryReadString(body, "error") ?? MapStatus(response.StatusCode);
            throw new OAuthTokenClientException(errorCode, "The provider token operation failed.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var accessToken = root.GetProperty("access_token").GetString();
            var tokenType = root.TryGetProperty("token_type", out var tokenTypeValue)
                ? tokenTypeValue.GetString() ?? "Bearer"
                : "Bearer";
            var expiresIn = root.TryGetProperty("expires_in", out var expiresValue)
                ? expiresValue.GetInt32()
                : 0;
            var scopeText = root.TryGetProperty("scope", out var scopeValue)
                ? scopeValue.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(accessToken) || expiresIn <= 0)
            {
                throw new OAuthTokenClientException("INVALID_TOKEN_RESPONSE", "The provider returned an incomplete token response.");
            }

            return new OAuthTokenSet(
                accessToken,
                root.TryGetProperty("refresh_token", out var refreshValue) ? refreshValue.GetString() : null,
                root.TryGetProperty("id_token", out var idTokenValue) ? idTokenValue.GetString() : null,
                tokenType,
                expiresIn,
                ParseScopes(scopeText));
        }
        catch (OAuthTokenClientException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new OAuthTokenClientException("INVALID_TOKEN_RESPONSE", "The provider returned an invalid token response.");
        }
    }

    private static IReadOnlySet<string> ParseScopes(string? scopeText)
        => new HashSet<string>(
            (scopeText ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);

    private static string? TryReadString(string body, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string MapStatus(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.Unauthorized => "invalid_client",
            HttpStatusCode.BadRequest => "invalid_grant",
            _ => "OAUTH_TOKEN_REQUEST_FAILED"
        };

    private async Task<string> ResolveRevocationEndpointAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(OpenAiOAuthOptions.OpenIdConfigurationEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var endpoint = document.RootElement.TryGetProperty("revocation_endpoint", out var value)
            ? value.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new OAuthTokenClientException("REVOCATION_ENDPOINT_UNAVAILABLE", "The provider revocation endpoint was not published.");
        }

        return endpoint;
    }
}

public sealed class OpenAiHttpJwksResolver : IOpenAiJwksResolver
{
    private const string JwksEndpoint = "https://auth.openai.com/.well-known/jwks.json";
    private readonly HttpClient _httpClient;

    public OpenAiHttpJwksResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<OpenAiJsonWebKey>> GetKeysAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(JwksEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            throw new OAuthValidationException("JWKS_INVALID", "The provider signing-key document is invalid.");
        }

        return keys.EnumerateArray()
            .Select(key => new OpenAiJsonWebKey(
                key.GetProperty("kid").GetString() ?? string.Empty,
                key.GetProperty("kty").GetString() ?? string.Empty,
                key.GetProperty("n").GetString() ?? string.Empty,
                key.GetProperty("e").GetString() ?? string.Empty,
                key.TryGetProperty("alg", out var alg) ? alg.GetString() : null))
            .ToArray();
    }
}
