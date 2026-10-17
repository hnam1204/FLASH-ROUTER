using System.Security.Cryptography;
using System.Text;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public sealed class OAuthTokenVaultStore
{
    private readonly ISecretVault _vault;

    public OAuthTokenVaultStore(ISecretVault vault)
    {
        _vault = vault;
    }

    public async Task StoreAsync(
        Guid connectionId,
        OAuthTokenSet tokenSet,
        CancellationToken cancellationToken = default)
    {
        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException("A connection identifier is required.", nameof(connectionId));
        }

        var tokenKeys = new[]
        {
            GetAccessKey(connectionId),
            GetRefreshKey(connectionId),
            GetIdTokenKey(connectionId)
        };
        var previousValues = new Dictionary<string, string?>();
        foreach (var key in tokenKeys)
        {
            previousValues[key] = await ReadValueAsync(key, cancellationToken);
        }

        try
        {
            await StoreValueAsync(GetAccessKey(connectionId), tokenSet.AccessToken, cancellationToken);
            if (!string.IsNullOrWhiteSpace(tokenSet.RefreshToken))
            {
                await StoreValueAsync(GetRefreshKey(connectionId), tokenSet.RefreshToken, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(tokenSet.IdToken))
            {
                await StoreValueAsync(GetIdTokenKey(connectionId), tokenSet.IdToken, cancellationToken);
            }
        }
        catch
        {
            foreach (var pair in previousValues)
            {
                try
                {
                    if (pair.Value is null)
                    {
                        await _vault.RemoveAsync(pair.Key, CancellationToken.None);
                    }
                    else
                    {
                        var bytes = Encoding.UTF8.GetBytes(pair.Value);
                        try
                        {
                            await _vault.StoreAsync(pair.Key, bytes, CancellationToken.None);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(bytes);
                        }
                    }
                }
                catch (SecretVaultException)
                {
                    // Preserve the original failure; an orphaned ciphertext is not a plaintext leak.
                }
            }

            throw;
        }
    }

    public async Task<OAuthTokenSet?> ReadAsync(Guid connectionId, IReadOnlySet<string> grantedScopes, int expiresIn, CancellationToken cancellationToken = default)
    {
        var accessToken = await ReadValueAsync(GetAccessKey(connectionId), cancellationToken);
        if (accessToken is null)
        {
            return null;
        }

        var refreshToken = await ReadValueAsync(GetRefreshKey(connectionId), cancellationToken);
        var idToken = await ReadValueAsync(GetIdTokenKey(connectionId), cancellationToken);
        return new OAuthTokenSet(accessToken, refreshToken, idToken, "Bearer", expiresIn, grantedScopes);
    }

    public async Task ClearAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        foreach (var key in new[]
        {
            GetAccessKey(connectionId),
            GetRefreshKey(connectionId),
            GetIdTokenKey(connectionId)
        })
        {
            await _vault.RemoveAsync(key, cancellationToken);
        }
    }

    public static string GetAccessKey(Guid connectionId) => $"connections/openai/{connectionId:N}/access-token";

    public static string GetRefreshKey(Guid connectionId) => $"connections/openai/{connectionId:N}/refresh-token";

    public static string GetIdTokenKey(Guid connectionId) => $"connections/openai/{connectionId:N}/id-token";

    private async Task StoreValueAsync(
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            await _vault.StoreAsync(key, bytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<string?> ReadValueAsync(string key, CancellationToken cancellationToken)
    {
        var payload = await _vault.ReadAsync(key, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(payload.Value.Span);
        }
        finally
        {
            var bytes = payload.Value.ToArray();
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
