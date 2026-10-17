using System.Security.Cryptography;
using System.Text;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.CredentialResolution;

public sealed class ProviderCredentialVaultStore : IProviderCredentialResolver
{
    private readonly ISecretVault _vault;

    public ProviderCredentialVaultStore(ISecretVault vault)
    {
        _vault = vault;
    }

    public async Task<ProviderCredential?> ResolveAsync(
        ProviderConnectionContext connection,
        CancellationToken cancellationToken = default)
    {
        var value = await ReadValueAsync(GetVaultKey(connection), cancellationToken);
        return value is null
            ? null
            : new ProviderCredential(
                connection.ProviderId,
                connection.ConnectionId,
                connection.CredentialId,
                connection.CredentialKind,
                value,
                connection.Scope);
    }

    public async Task StoreAsync(
        ProviderConnectionContext connection,
        string credentialValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialValue))
        {
            throw new ArgumentException("A provider credential is required.", nameof(credentialValue));
        }

        var bytes = Encoding.UTF8.GetBytes(credentialValue);
        try
        {
            await _vault.StoreAsync(GetVaultKey(connection), bytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task RemoveAsync(ProviderConnectionContext connection, CancellationToken cancellationToken = default)
        => _vault.RemoveAsync(GetVaultKey(connection), cancellationToken).AsTask();

    public static string GetVaultKey(ProviderConnectionContext connection)
        => GetVaultKey(connection.ProviderId, connection.ConnectionId, connection.CredentialKind);

    public static string GetVaultKey(
        string providerId,
        Guid connectionId,
        ProviderCredentialKind credentialKind)
    {
        var normalizedProvider = providerId.Trim().ToLowerInvariant();
        return $"connections/api/{normalizedProvider}/{connectionId:N}/{credentialKind}";
    }

    private async Task<string?> ReadValueAsync(string key, CancellationToken cancellationToken)
    {
        var payload = await _vault.ReadAsync(key, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        var bytes = payload.Value.ToArray();
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
