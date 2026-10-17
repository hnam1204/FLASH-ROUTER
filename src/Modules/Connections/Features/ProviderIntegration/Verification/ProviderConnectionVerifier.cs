using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

public sealed class ProviderConnectionVerifier : IProviderConnectionVerifier
{
    private readonly IProviderRegistry _registry;
    private readonly IProviderCapabilityResolver _capabilities;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IClock _clock;

    public ProviderConnectionVerifier(
        IProviderRegistry registry,
        IProviderCapabilityResolver capabilities,
        IProviderCredentialResolver credentials,
        IClock clock)
    {
        _registry = registry;
        _capabilities = capabilities;
        _credentials = credentials;
        _clock = clock;
    }

    public async Task<ProviderCheckResult> VerifyAsync(
        ProviderConnectionContext connection,
        ProviderCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var started = _clock.UtcNow;
        var correlation = Guid.NewGuid().ToString("N");
        if (!_registry.TryResolve(connection.ProviderId, out var adapter) || adapter is null)
        {
            return Failure(connection, ProviderOperationStatus.ConfigurationError, "PROVIDER_NOT_REGISTERED", "The provider is not registered.", started, correlation);
        }

        if (connection.Status is ConnectionStatus.Disconnected or ConnectionStatus.Revoked or ConnectionStatus.Unauthorized)
        {
            return Failure(connection, ProviderOperationStatus.Unauthorized, "CONNECTION_NOT_AUTHORIZED", "The provider connection is not authorized for checking.", started, correlation);
        }

        ProviderCredential? credential;
        try
        {
            credential = await _credentials.ResolveAsync(connection, cancellationToken);
        }
        catch (SecretVaultException)
        {
            return Failure(connection, ProviderOperationStatus.ConfigurationError, "CREDENTIAL_VAULT_UNAVAILABLE", "The credential vault is unavailable; the provider was not contacted.", started, correlation);
        }

        if (credential is null)
        {
            return Failure(connection, ProviderOperationStatus.ConfigurationError, "CREDENTIAL_NOT_FOUND", "The provider credential is not available in the vault.", started, correlation);
        }

        var supported = _capabilities.Resolve(connection, adapter.Descriptor);
        var requested = request.Capabilities.Count == 0
            ? supported
            : request.Capabilities;
        var filtered = requested.Where(supported.Contains).ToHashSet();
        if (request.Capabilities.Count > 0 && filtered.Count == 0)
        {
            var unsupported = requested.Select(capability => ProviderOperationResult.Failure(
                ProviderOperationStatus.Unsupported,
                "CAPABILITY_UNSUPPORTED",
                "The requested capability is not supported for this provider credential scope.",
                connection,
                _clock.UtcNow,
                correlation,
                capability)).ToArray();
            return new ProviderCheckResult(
                ProviderOperationStatus.Unsupported,
                "CAPABILITY_UNSUPPORTED",
                "The requested capabilities are not supported for this provider credential scope.",
                connection.ProviderId,
                connection.ConnectionId,
                started,
                _clock.UtcNow,
                correlation,
                [],
                unsupported);
        }

        var result = await adapter.CheckAsync(connection, credential, request with { Capabilities = filtered }, cancellationToken);
        return result;
    }

    private static ProviderCheckResult Failure(
        ProviderConnectionContext connection,
        ProviderOperationStatus status,
        string code,
        string message,
        DateTimeOffset started,
        string correlation)
        => new(status, code, message, connection.ProviderId, connection.ConnectionId, started, DateTimeOffset.UtcNow, correlation, [], []);
}
