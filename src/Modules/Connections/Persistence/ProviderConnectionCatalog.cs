using System.Collections.Concurrent;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.CredentialResolution;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Persistence;

public sealed class InMemoryProviderConnectionCatalog : IProviderConnectionCatalog
{
    private readonly ConcurrentDictionary<Guid, ProviderConnectionEntry> _connections = new();
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderRegistry _registry;

    public InMemoryProviderConnectionCatalog(
        IProviderCredentialResolver credentials,
        IProviderRegistry registry)
    {
        _credentials = credentials;
        _registry = registry;
    }

    public async Task<ProviderConnectionSummary> CreateApiConnectionAsync(
        ProviderConnectionCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _registry.Resolve(request.ProviderId).Descriptor;
        if (request.CredentialKind is ProviderCredentialKind.OAuthAccessToken or ProviderCredentialKind.OAuthRefreshToken)
        {
            throw new ProviderConfigurationException("API_CREDENTIAL_KIND_INVALID", "An API connection requires an API credential kind.");
        }

        var connectionId = Guid.NewGuid();
        var credentialId = $"cred_{Guid.NewGuid():N}";
        var createdAt = DateTimeOffset.UtcNow;
        var context = new ProviderConnectionContext(
            connectionId,
            request.AccountId,
            descriptor.ProviderId,
            ConnectionKind.ApiKey,
            request.CredentialKind,
            credentialId,
            ConnectionStatus.CredentialStored,
            request.AuthorizedScopes ?? new HashSet<string>(StringComparer.Ordinal),
            request.Scope,
            createdAt);
        await _credentials.StoreAsync(context, request.CredentialValue, cancellationToken);
        _connections[connectionId] = new ProviderConnectionEntry(context, request.DisplayLabel);
        return ToSummary(context, request.DisplayLabel);
    }

    public Task<ProviderConnectionContext?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_connections.TryGetValue(connectionId, out var entry) ? entry.Context : null);
    }

    public Task<IReadOnlyList<ProviderConnectionSummary>> GetEligibleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProviderConnectionSummary>>(_connections.Values
            .Where(entry => entry.Context.Status is ConnectionStatus.CredentialStored or ConnectionStatus.Authorized)
            .Select(entry => ToSummary(entry.Context, entry.DisplayLabel))
            .OrderBy(entry => entry.ProviderId, StringComparer.Ordinal)
            .ToArray());
    }

    public Task<IReadOnlyList<ProviderConnectionSummary>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProviderConnectionSummary>>(_connections.Values
            .Where(entry => entry.Context.AccountId == accountId)
            .Select(entry => ToSummary(entry.Context, entry.DisplayLabel))
            .OrderByDescending(entry => entry.Status == ConnectionStatus.Authorized)
            .ThenByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.ConnectionId)
            .ToArray());
    }

    public Task MarkStatusAsync(Guid connectionId, ConnectionStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connections.TryGetValue(connectionId, out var entry))
        {
            throw new ProviderConfigurationException("CONNECTION_NOT_FOUND", "The provider connection was not found.");
        }

        _connections[connectionId] = entry with { Context = entry.Context with { Status = status } };
        return Task.CompletedTask;
    }

    private static ProviderConnectionSummary ToSummary(ProviderConnectionContext context, string displayLabel)
        => new(context.ConnectionId, context.AccountId, context.ProviderId, displayLabel, context.ConnectionKind, context.CredentialKind, context.Status, context.CredentialId, context.CreatedAtUtc);

    private sealed record ProviderConnectionEntry(ProviderConnectionContext Context, string DisplayLabel);
}

public sealed class SqliteProviderConnectionCatalog : IProviderConnectionCatalog
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderRegistry _registry;

    public SqliteProviderConnectionCatalog(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        IProviderCredentialResolver credentials,
        IProviderRegistry registry)
    {
        _contextFactory = contextFactory;
        _credentials = credentials;
        _registry = registry;
    }

    public async Task<ProviderConnectionSummary> CreateApiConnectionAsync(
        ProviderConnectionCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _registry.Resolve(request.ProviderId).Descriptor;
        if (request.CredentialKind is ProviderCredentialKind.OAuthAccessToken or ProviderCredentialKind.OAuthRefreshToken)
        {
            throw new ProviderConfigurationException("API_CREDENTIAL_KIND_INVALID", "An API connection requires an API credential kind.");
        }

        var connectionId = Guid.NewGuid();
        var credentialId = $"cred_{Guid.NewGuid():N}";
        var createdAt = DateTimeOffset.UtcNow;
        var context = new ProviderConnectionContext(
            connectionId,
            request.AccountId,
            descriptor.ProviderId,
            ConnectionKind.ApiKey,
            request.CredentialKind,
            credentialId,
            ConnectionStatus.CredentialStored,
            request.AuthorizedScopes ?? new HashSet<string>(StringComparer.Ordinal),
            request.Scope,
            createdAt);

        await _credentials.StoreAsync(context, request.CredentialValue, cancellationToken);
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var connection = new ProviderConnectionRecord(
                connectionId,
                request.AccountId,
                descriptor.ProviderId,
                ConnectionKind.ApiKey,
                string.IsNullOrWhiteSpace(request.DisplayLabel) ? descriptor.DisplayName : request.DisplayLabel,
                null,
                ConnectionStatus.CredentialStored,
                createdAt,
                createdAt);
            var vaultKey = ProviderCredentialVaultStore.GetVaultKey(descriptor.ProviderId, connectionId, request.CredentialKind);
            var reference = new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                connectionId,
                descriptor.ProviderId,
                credentialId,
                request.CredentialKind,
                vaultKey,
                request.Scope,
                createdAt,
                createdAt);
            var sync = new ProviderSyncStateRecord(Guid.NewGuid(), connectionId, descriptor.ProviderId, false, null, null, 0, createdAt);
            db.Add(connection);
            db.Add(reference);
            db.Add(sync);
            await db.SaveChangesAsync(cancellationToken);
            return new ProviderConnectionSummary(connectionId, request.AccountId, descriptor.ProviderId, connection.DisplayLabel, ConnectionKind.ApiKey, request.CredentialKind, ConnectionStatus.CredentialStored, credentialId, createdAt);
        }
        catch
        {
            await _credentials.RemoveAsync(context, CancellationToken.None);
            throw;
        }
    }

    public async Task<ProviderConnectionContext?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.Set<ProviderConnectionRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == connectionId, cancellationToken);
        var reference = await db.Set<ProviderCredentialReferenceRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.ConnectionId == connectionId, cancellationToken);
        if (connection is null || reference is null)
        {
            return null;
        }

        return new ProviderConnectionContext(
            connection.Id,
            connection.AccountId,
            connection.ProviderId,
            connection.ConnectionKind,
            reference.CredentialKind,
            reference.CredentialId,
            connection.Status,
            new HashSet<string>(StringComparer.Ordinal),
            reference.Scope,
            connection.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<ProviderConnectionSummary>> GetEligibleAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var items = await (
            from connection in db.Set<ProviderConnectionRecord>().AsNoTracking()
            join reference in db.Set<ProviderCredentialReferenceRecord>().AsNoTracking()
                on connection.Id equals reference.ConnectionId
            where connection.Status == ConnectionStatus.CredentialStored || connection.Status == ConnectionStatus.Authorized
            select new ProviderConnectionSummary(
                connection.Id,
                connection.AccountId,
                connection.ProviderId,
                connection.DisplayLabel,
                connection.ConnectionKind,
                reference.CredentialKind,
                connection.Status,
                reference.CredentialId,
                connection.CreatedAtUtc)).ToArrayAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset ordering. The filtered set is
        // materialized first, then deterministically ordered without SQL ORDER BY.
        return items
            .OrderBy(item => item.ProviderId, StringComparer.Ordinal)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.ConnectionId)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProviderConnectionSummary>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from connection in db.Set<ProviderConnectionRecord>().AsNoTracking()
            join reference in db.Set<ProviderCredentialReferenceRecord>().AsNoTracking()
                on connection.Id equals reference.ConnectionId into references
            from reference in references.DefaultIfEmpty()
            where connection.AccountId == accountId
            orderby connection.Status == ConnectionStatus.Authorized descending,
                connection.CreatedAtUtc descending,
                connection.Id descending
            select new ProviderConnectionSummary(
                connection.Id,
                connection.AccountId,
                connection.ProviderId,
                connection.DisplayLabel,
                connection.ConnectionKind,
                reference == null ? ProviderCredentialKind.ManualWebCredential : reference.CredentialKind,
                connection.Status,
                reference == null ? string.Empty : reference.CredentialId,
                connection.CreatedAtUtc)).ToArrayAsync(cancellationToken);
    }

    public async Task MarkStatusAsync(Guid connectionId, ConnectionStatus status, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.Set<ProviderConnectionRecord>().SingleOrDefaultAsync(item => item.Id == connectionId, cancellationToken)
            ?? throw new ProviderConfigurationException("CONNECTION_NOT_FOUND", "The provider connection was not found.");
        connection.MarkStatus(status, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}
