using System.Text;
using FlashAI.Foundation.Security;

namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public sealed class OpenAiHostIdentifierProvider
{
    private const string HostIdentifierKey = "connections/openai/host-identifier";
    private readonly ISecretVault _vault;

    public OpenAiHostIdentifierProvider(ISecretVault vault)
    {
        _vault = vault;
    }

    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _vault.ReadAsync(HostIdentifierKey, cancellationToken);
        if (existing is not null)
        {
            var value = Encoding.UTF8.GetString(existing.Value.Span);
            if (value.StartsWith("urn:uuid:", StringComparison.Ordinal)
                && Guid.TryParse(value[9..], out _))
            {
                return value;
            }

            throw new SecretVaultException(
                "OAUTH_HOST_IDENTIFIER_CORRUPT",
                "The persisted OAuth host identifier is invalid.");
        }

        var identifier = $"urn:uuid:{Guid.NewGuid():D}";
        var bytes = Encoding.UTF8.GetBytes(identifier);
        try
        {
            await _vault.StoreAsync(HostIdentifierKey, bytes, cancellationToken);
            return identifier;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
