using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed class InMemoryAccountConnectionsReader : IAccountConnectionsReader
{
    private readonly IProviderConnectionCatalog _providerConnections;
    private readonly IOAuthConnectionStore _oauthConnections;
    private readonly IProviderRegistry _registry;

    public InMemoryAccountConnectionsReader(
        IProviderConnectionCatalog providerConnections,
        IOAuthConnectionStore oauthConnections,
        IProviderRegistry registry)
    {
        _providerConnections = providerConnections;
        _oauthConnections = oauthConnections;
        _registry = registry;
    }

    public async Task<IReadOnlyList<AccountConnectionSummary>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var apiConnections = await _providerConnections.ListForAccountAsync(accountId, cancellationToken);
        var oauthConnections = await _oauthConnections.ListForAccountAsync(accountId, cancellationToken);
        var results = new List<AccountConnectionSummary>(apiConnections.Count + oauthConnections.Count);

        foreach (var connection in apiConnections)
        {
            var context = await _providerConnections.GetAsync(connection.ConnectionId, cancellationToken);
            results.Add(ToApiSummary(connection, context, GetProviderName(connection.ProviderId)));
        }

        results.AddRange(oauthConnections.Select(connection => ToOAuthSummary(connection, GetProviderName(connection.ProviderId))));
        return AccountConnectionOrdering.Order(results);
    }

    private string GetProviderName(string providerId)
        => _registry.Descriptors.FirstOrDefault(item => item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? ProviderDisplayNames.Get(providerId);

    private static AccountConnectionSummary ToApiSummary(
        ProviderConnectionSummary connection,
        ProviderConnectionContext? context,
        string providerName)
        => new(
            connection.AccountId,
            connection.ConnectionId,
            connection.ProviderId,
            providerName,
            null,
            connection.ConnectionKind,
            connection.CredentialKind,
            connection.Status,
            null,
            context?.Scope,
            connection.CreatedAtUtc,
            null,
            null,
            null,
            null,
            string.IsNullOrWhiteSpace(connection.CredentialId)
                ? ConnectionCredentialAvailability.MissingReference
                : ConnectionCredentialAvailability.StoredReference,
            connection.ConnectionKind == ConnectionKind.Manual
                ? ConnectionProvenance.LocalMetadata
                : ConnectionProvenance.ProviderApiCredential,
            [],
            null);

    private static AccountConnectionSummary ToOAuthSummary(OAuthConnectionSnapshot connection, string providerName)
        => new(
            connection.AccountId,
            connection.ConnectionId,
            connection.ProviderId,
            providerName,
            connection.RegistrationId,
            ConnectionKind.WebOAuth,
            ProviderCredentialKind.OAuthAccessToken,
            connection.Status,
            connection.ProviderSubject,
            null,
            connection.AuthorizedAtUtc,
            connection.AuthorizedAtUtc,
            connection.LastVerifiedAtUtc,
            null,
            connection.AccessTokenExpiresAtUtc,
            ConnectionCredentialAvailability.StoredReference,
            ConnectionProvenance.OfficialOAuth,
            connection.GrantedScopes.OrderBy(value => value, StringComparer.Ordinal).ToArray());
}

public sealed class SqliteAccountConnectionsReader : IAccountConnectionsReader
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly IProviderRegistry _registry;

    public SqliteAccountConnectionsReader(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        IProviderRegistry registry)
    {
        _contextFactory = contextFactory;
        _registry = registry;
    }

    public async Task<IReadOnlyList<AccountConnectionSummary>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connections = await db.Set<ProviderConnectionRecord>()
            .AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .ToArrayAsync(cancellationToken);
        var accountEmail = await ReadAccountEmailAsync(db, accountId, cancellationToken);
        var connectionIds = connections.Select(item => item.Id).ToArray();

        var credentialReferences = await db.Set<ProviderCredentialReferenceRecord>()
            .AsNoTracking()
            .Where(item => connectionIds.Contains(item.ConnectionId))
            .ToDictionaryAsync(item => item.ConnectionId, cancellationToken);
        var sessions = await db.Set<ConnectionSessionRecord>()
            .AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .ToDictionaryAsync(item => item.ConnectionId, cancellationToken);
        var tokenReferenceIds = await db.Set<TokenVaultReferenceRecord>()
            .AsNoTracking()
            .Where(item => connectionIds.Contains(item.ConnectionId))
            .Select(item => item.ConnectionId)
            .ToArrayAsync(cancellationToken);
        var tokenReferences = tokenReferenceIds.ToHashSet();

        var results = connections.Select(connection =>
        {
            sessions.TryGetValue(connection.Id, out var session);
            credentialReferences.TryGetValue(connection.Id, out var credential);
            return session is not null
                ? ToOAuthSummary(connection, session, tokenReferences.Contains(connection.Id), GetProviderName(connection.ProviderId))
                : ToApiSummary(connection, credential, GetProviderName(connection.ProviderId), accountEmail);
        }).ToList();

        // Keep metadata readable even if a legacy database contains a session without its projection row.
        foreach (var session in sessions.Values.Where(item => !connections.Any(connection => connection.Id == item.ConnectionId)))
        {
            results.Add(ToOAuthSummary(session, tokenReferences.Contains(session.ConnectionId), GetProviderName(session.ProviderId)));
        }

        return AccountConnectionOrdering.Order(results);
    }

    private string GetProviderName(string providerId)
        => _registry.Descriptors.FirstOrDefault(item => item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? ProviderDisplayNames.Get(providerId);

    private static AccountConnectionSummary ToApiSummary(
        ProviderConnectionRecord connection,
        ProviderCredentialReferenceRecord? credential,
        string providerName,
        string? accountEmail = null)
        => new(
            connection.AccountId,
            connection.Id,
            connection.ProviderId,
            providerName,
            null,
            connection.ConnectionKind,
            credential?.CredentialKind,
            connection.Status,
            connection.ConnectionKind == ConnectionKind.Manual
                ? null
                : connection.ExternalIdentity,
            credential?.Scope,
            connection.CreatedAtUtc,
            null,
            null,
            connection.LastCheckedAtUtc,
            null,
            credential is null
                ? ConnectionCredentialAvailability.MissingReference
                : ConnectionCredentialAvailability.StoredReference,
            connection.ConnectionKind == ConnectionKind.Manual
                ? ConnectionProvenance.LocalMetadata
                : ConnectionProvenance.ProviderApiCredential,
            [],
            connection.ConnectionKind == ConnectionKind.Manual
                ? connection.ExternalIdentity ?? accountEmail
                : null);

    private static AccountConnectionSummary ToOAuthSummary(
        ProviderConnectionRecord connection,
        ConnectionSessionRecord session,
        bool hasTokenReference,
        string providerName)
        => new(
            connection.AccountId,
            connection.Id,
            connection.ProviderId,
            providerName,
            session.RegistrationId,
            connection.ConnectionKind,
            ProviderCredentialKind.OAuthAccessToken,
            session.Status,
            session.ProviderSubject,
            null,
            connection.CreatedAtUtc,
            session.AuthorizedAtUtc,
            session.LastVerifiedAtUtc,
            connection.LastCheckedAtUtc,
            session.AccessTokenExpiresAtUtc,
            hasTokenReference
                ? ConnectionCredentialAvailability.StoredReference
                : ConnectionCredentialAvailability.MissingReference,
            ConnectionProvenance.OfficialOAuth,
            SplitScopes(session.GrantedScopes));

    private static AccountConnectionSummary ToOAuthSummary(
        ConnectionSessionRecord session,
        bool hasTokenReference,
        string providerName)
        => new(
            session.AccountId,
            session.ConnectionId,
            session.ProviderId,
            providerName,
            session.RegistrationId,
            ConnectionKind.WebOAuth,
            ProviderCredentialKind.OAuthAccessToken,
            session.Status,
            session.ProviderSubject,
            null,
            session.AuthorizedAtUtc,
            session.AuthorizedAtUtc,
            session.LastVerifiedAtUtc,
            null,
            session.AccessTokenExpiresAtUtc,
            hasTokenReference
                ? ConnectionCredentialAvailability.StoredReference
                : ConnectionCredentialAvailability.MissingReference,
            ConnectionProvenance.OfficialOAuth,
            SplitScopes(session.GrantedScopes));

    private static IReadOnlyList<string> SplitScopes(string scopes)
        => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static async Task<string?> ReadAccountEmailAsync(
        FlashAiDbContext db,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var databaseConnection = db.Database.GetDbConnection();
        if (databaseConnection.State != System.Data.ConnectionState.Open)
        {
            await databaseConnection.OpenAsync(cancellationToken);
        }

        await using var command = databaseConnection.CreateCommand();
        command.CommandText = "SELECT Email FROM AccountProfiles WHERE Id = $accountId LIMIT 1";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$accountId";
        parameter.Value = accountId;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string email ? email : null;
    }
}

internal static class AccountConnectionOrdering
{
    public static IReadOnlyList<AccountConnectionSummary> Order(IEnumerable<AccountConnectionSummary> connections)
        => connections
            .OrderByDescending(item => item.Status == ConnectionStatus.Authorized)
            .ThenByDescending(item => item.AuthorizedAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.ConnectionId)
            .ToArray();
}

internal static class ProviderDisplayNames
{
    public static string Get(string providerId)
        => providerId.ToLowerInvariant() switch
        {
            "chatgpt" or "openai-chatgpt" or "openai-oauth" => "ChatGPT",
            "openai-platform" => "OpenAI Platform",
            "openrouter" => "OpenRouter",
            "anthropic" => "Anthropic",
            "gemini" => "Gemini",
            "deepseek" => "DeepSeek",
            "groq" => "Groq",
            _ => providerId
        };
}
