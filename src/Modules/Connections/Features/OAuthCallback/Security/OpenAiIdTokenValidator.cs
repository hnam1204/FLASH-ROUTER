using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;

namespace FlashAI.Modules.Connections.Features.OAuthCallback.Security;

public sealed class OpenAiIdTokenValidator
{
    private readonly IOpenAiJwksResolver _jwksResolver;
    private readonly IClock _clock;

    public OpenAiIdTokenValidator(IOpenAiJwksResolver jwksResolver, IClock clock)
    {
        _jwksResolver = jwksResolver;
        _clock = clock;
    }

    public async Task<VerifiedProviderIdentity> ValidateAsync(
        string idToken,
        string expectedClientId,
        string expectedNonce,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken)
            || string.IsNullOrWhiteSpace(expectedClientId)
            || string.IsNullOrWhiteSpace(expectedNonce))
        {
            throw new OAuthValidationException("INVALID_ID_TOKEN", "The ID token validation input is incomplete.");
        }

        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new OAuthValidationException("INVALID_ID_TOKEN", "The ID token format is invalid.");
        }

        try
        {
            var header = ParseObject(parts[0]);
            var payload = ParseObject(parts[1]);
            var signature = OAuthSecurityPrimitives.Base64UrlDecode(parts[2]);
            var algorithm = header.GetProperty("alg").GetString();
            var keyId = header.GetProperty("kid").GetString();
            if (!string.Equals(algorithm, "RS256", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(keyId))
            {
                throw new OAuthValidationException("INVALID_ID_TOKEN", "The ID token signing algorithm is not supported.");
            }

            var key = (await _jwksResolver.GetKeysAsync(cancellationToken))
                .FirstOrDefault(candidate => candidate.KeyId == keyId && candidate.KeyType == "RSA");
            if (key is null)
            {
                throw new OAuthValidationException("INVALID_ID_TOKEN_SIGNATURE", "The ID token signing key is not recognized.");
            }

            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = OAuthSecurityPrimitives.Base64UrlDecode(key.Modulus),
                Exponent = OAuthSecurityPrimitives.Base64UrlDecode(key.Exponent)
            });

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            try
            {
                if (!rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    throw new OAuthValidationException("INVALID_ID_TOKEN_SIGNATURE", "The ID token signature is invalid.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signingInput);
                CryptographicOperations.ZeroMemory(signature);
            }

            var issuer = payload.GetProperty("iss").GetString();
            if (!string.Equals(issuer, OpenAiOAuthOptions.Issuer, StringComparison.Ordinal))
            {
                throw new OAuthValidationException("INVALID_ID_TOKEN_ISSUER", "The ID token issuer is invalid.");
            }

            if (!AudienceContains(payload, expectedClientId))
            {
                throw new OAuthValidationException("INVALID_ID_TOKEN_AUDIENCE", "The ID token audience is invalid.");
            }

            var now = _clock.UtcNow.ToUnixTimeSeconds();
            if (!payload.TryGetProperty("exp", out var expiry)
                || expiry.ValueKind != JsonValueKind.Number
                || expiry.GetInt64() <= now)
            {
                throw new OAuthValidationException("EXPIRED_ID_TOKEN", "The ID token has expired.");
            }

            if (payload.TryGetProperty("nbf", out var notBefore)
                && notBefore.ValueKind == JsonValueKind.Number
                && notBefore.GetInt64() > now)
            {
                throw new OAuthValidationException("ID_TOKEN_NOT_ACTIVE", "The ID token is not active yet.");
            }

            if (!payload.TryGetProperty("nonce", out var nonce)
                || !OAuthSecurityPrimitives.FixedTimeEquals(expectedNonce, nonce.GetString() ?? string.Empty))
            {
                throw new OAuthValidationException("NONCE_MISMATCH", "The ID token nonce does not match the authorization attempt.");
            }

            var subject = payload.TryGetProperty("sub", out var subjectValue) ? subjectValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new OAuthValidationException("MISSING_PROVIDER_SUBJECT", "The ID token does not contain a provider subject.");
            }

            return new VerifiedProviderIdentity(
                issuer!,
                subject!,
                payload.TryGetProperty("email", out var email) ? email.GetString() : null,
                payload.TryGetProperty("name", out var name) ? name.GetString() : null);
        }
        catch (OAuthValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException or KeyNotFoundException)
        {
            throw new OAuthValidationException("INVALID_ID_TOKEN", "The ID token could not be validated.", exception);
        }
    }

    private static JsonElement ParseObject(string encoded)
    {
        var json = Encoding.UTF8.GetString(OAuthSecurityPrimitives.Base64UrlDecode(encoded));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool AudienceContains(JsonElement payload, string expectedClientId)
    {
        if (!payload.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        return audience.ValueKind switch
        {
            JsonValueKind.String => audience.GetString() == expectedClientId,
            JsonValueKind.Array => audience.EnumerateArray().Any(item => item.GetString() == expectedClientId),
            _ => false
        };
    }
}

public sealed class OAuthValidationException : Exception
{
    public OAuthValidationException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
