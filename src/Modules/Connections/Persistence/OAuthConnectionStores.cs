using System.Collections.Concurrent;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Persistence;

public sealed class InMemoryOAuthConnectionStore : IOAuthConnectionStore
{
    private readonly ConcurrentDictionary<Guid, OAuthConnectionSnapshot> _connections = new();
    private readonly ConcurrentDictionary<(string ProviderId, string ClientId), Guid> _registrations = new();

    public Task SaveAuthorizedAsync(
        Guid accountId,
        Guid connectionId,
        Guid registrationId,
        string providerId,
        string providerSubject,
        string issuedClientId,
        string hostIdentifier,
        string agentName,
        IReadOnlySet<string> grantedScopes,
        DateTimeOffset authorizedAtUtc,
        DateTimeOffset accessTokenExpiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_registrations.TryGetValue((providerId, issuedClientId), out var existingConnectionId)
            && _connections.TryGetValue(existingConnectionId, out var existing)
            && existing.AccountId != accountId)
        {
            throw new OAuthConnectionStoreException("REGISTRATION_ACCOUNT_MISMATCH", "The issued client registration belongs to another local account.");
        }

        _registrations[(providerId, issuedClientId)] = connectionId;
        _connections[connectionId] = new OAuthConnectionSnapshot(
            accountId,
            connectionId,
            registrationId,
            providerId,
            providerSubject,
            issuedClientId,
            hostIdentifier,
            new HashSet<string>(grantedScopes, StringComparer.Ordinal),
            ConnectionStatus.Authorized,
            authorizedAtUtc,
            accessTokenExpiresAtUtc,
            null,
            authorizedAtUtc);
        return Task.CompletedTask;
    }

    public Task<OAuthConnectionSnapshot?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connections.TryGetValue(connectionId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<OAuthConnectionSnapshot?> GetForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _connections.Values
            .Where(item => item.AccountId == accountId)
            .OrderByDescending(item => item.AuthorizedAtUtc)
            .FirstOrDefault();
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<OAuthConnectionSnapshot>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OAuthConnectionSnapshot>>(_connections.Values
            .Where(item => item.AccountId == accountId)
            .OrderByDescending(item => item.Status == ConnectionStatus.Authorized)
            .ThenByDescending(item => item.AuthorizedAtUtc)
            .ThenByDescending(item => item.ConnectionId)
            .ToArray());
    }

    public Task MarkStatusAsync(Guid connectionId, ConnectionStatus status, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connections.TryGetValue(connectionId, out var snapshot))
        {
            throw new OAuthConnectionStoreException("CONNECTION_NOT_FOUND", "The connection was not found.");
        }

        _connections[connectionId] = snapshot with { Status = status };
        return Task.CompletedTask;
    }

    public Task MarkRefreshedAsync(Guid connectionId, DateTimeOffset refreshedAtUtc, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connections.TryGetValue(connectionId, out var snapshot))
        {
            throw new OAuthConnectionStoreException("CONNECTION_NOT_FOUND", "The connection was not found.");
        }

        _connections[connectionId] = snapshot with
        {
            Status = ConnectionStatus.Authorized,
            AccessTokenExpiresAtUtc = expiresAtUtc,
            LastRefreshAtUtc = refreshedAtUtc
        };
        return Task.CompletedTask;
    }
}

public sealed class SqliteOAuthConnectionStore : IOAuthConnectionStore
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteOAuthConnectionStore(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task SaveAuthorizedAsync(
        Guid accountId,
        Guid connectionId,
        Guid registrationId,
        string providerId,
        string providerSubject,
        string issuedClientId,
        string hostIdentifier,
        string agentName,
        IReadOnlySet<string> grantedScopes,
        DateTimeOffset authorizedAtUtc,
        DateTimeOffset accessTokenExpiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var registration = await db.Set<OAuthRegistrationRecord>()
                .SingleOrDefaultAsync(
                    item => item.ProviderId == providerId && item.IssuedClientId == issuedClientId,
                    cancellationToken);
            if (registration is not null && registration.AccountId != accountId)
            {
                throw new OAuthConnectionStoreException("REGISTRATION_ACCOUNT_MISMATCH", "The issued client registration belongs to another local account.");
            }

            registration ??= new OAuthRegistrationRecord(
                registrationId,
                accountId,
                providerId,
                issuedClientId,
                hostIdentifier,
                agentName,
                providerSubject,
                authorizedAtUtc,
                authorizedAtUtc);
            if (db.Entry(registration).State == EntityState.Detached)
            {
                db.Add(registration);
            }

            var providerConnection = new ProviderConnectionRecord(
                connectionId,
                accountId,
                providerId,
                ConnectionKind.WebOAuth,
                "OpenAI ChatGPT",
                providerSubject,
                ConnectionStatus.Authorized,
                authorizedAtUtc,
                authorizedAtUtc);
            var session = new ConnectionSessionRecord(
                Guid.NewGuid(),
                accountId,
                providerId,
                registration.Id,
                connectionId,
                providerSubject,
                issuedClientId,
                hostIdentifier,
                string.Join(' ', grantedScopes.OrderBy(value => value, StringComparer.Ordinal)),
                ConnectionStatus.Authorized,
                authorizedAtUtc,
                accessTokenExpiresAtUtc,
                null,
                authorizedAtUtc,
                authorizedAtUtc);
            var references = new TokenVaultReferenceRecord(
                Guid.NewGuid(),
                connectionId,
                Features.ConnectProvider.Application.OAuthTokenVaultStore.GetAccessKey(connectionId),
                Features.ConnectProvider.Application.OAuthTokenVaultStore.GetRefreshKey(connectionId),
                Features.ConnectProvider.Application.OAuthTokenVaultStore.GetIdTokenKey(connectionId),
                authorizedAtUtc,
                authorizedAtUtc);

            db.Add(providerConnection);
            db.Add(session);
            db.Add(references);
            db.Add(new ConnectionCheckRecord(Guid.NewGuid(), connectionId, "authorization", "OpenAI OAuth ID token", "validated", authorizedAtUtc));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OAuthConnectionStoreException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            throw new OAuthConnectionStoreException("CONNECTION_PERSISTENCE_FAILED", "The authorized connection could not be persisted.");
        }
    }

    public async Task<OAuthConnectionSnapshot?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.Set<ConnectionSessionRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == connectionId, cancellationToken);
        return item is null
            ? null
            : new OAuthConnectionSnapshot(
                item.AccountId,
                item.ConnectionId,
                item.RegistrationId,
                item.ProviderId,
                item.ProviderSubject,
                item.IssuedClientId,
                item.HostIdentifier,
                item.GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal),
                item.Status,
                item.AuthorizedAtUtc,
                item.AccessTokenExpiresAtUtc,
                item.LastRefreshAtUtc,
                item.LastVerifiedAtUtc);
    }

    public async Task<OAuthConnectionSnapshot?> GetForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.Set<ConnectionSessionRecord>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.AuthorizedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return item is null
            ? null
            : new OAuthConnectionSnapshot(
                item.AccountId,
                item.ConnectionId,
                item.RegistrationId,
                item.ProviderId,
                item.ProviderSubject,
                item.IssuedClientId,
                item.HostIdentifier,
                item.GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal),
                item.Status,
                item.AuthorizedAtUtc,
                item.AccessTokenExpiresAtUtc,
                item.LastRefreshAtUtc,
                item.LastVerifiedAtUtc);
    }

    public async Task<IReadOnlyList<OAuthConnectionSnapshot>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.Set<ConnectionSessionRecord>()
            .AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .ToArrayAsync(cancellationToken);

        var items = records.Select(item => new OAuthConnectionSnapshot(
                item.AccountId,
                item.ConnectionId,
                item.RegistrationId,
                item.ProviderId,
                item.ProviderSubject,
                item.IssuedClientId,
                item.HostIdentifier,
                item.GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(),
                item.Status,
                item.AuthorizedAtUtc,
                item.AccessTokenExpiresAtUtc,
                item.LastRefreshAtUtc,
                item.LastVerifiedAtUtc))
            .ToArray();

        return items
            .OrderByDescending(item => item.Status == ConnectionStatus.Authorized)
            .ThenByDescending(item => item.AuthorizedAtUtc)
            .ThenByDescending(item => item.ConnectionId)
            .ToArray();
    }

    public async Task MarkStatusAsync(Guid connectionId, ConnectionStatus status, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Set<ConnectionSessionRecord>().SingleOrDefaultAsync(x => x.ConnectionId == connectionId, cancellationToken)
            ?? throw new OAuthConnectionStoreException("CONNECTION_NOT_FOUND", "The connection was not found.");
        session.MarkStatus(status, updatedAtUtc);
        var providerConnection = await db.Set<ProviderConnectionRecord>().SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken);
        providerConnection?.MarkStatus(status, updatedAtUtc);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRefreshedAsync(Guid connectionId, DateTimeOffset refreshedAtUtc, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Set<ConnectionSessionRecord>().SingleOrDefaultAsync(x => x.ConnectionId == connectionId, cancellationToken)
            ?? throw new OAuthConnectionStoreException("CONNECTION_NOT_FOUND", "The connection was not found.");
        session.MarkRefreshed(refreshedAtUtc, expiresAtUtc);
        var providerConnection = await db.Set<ProviderConnectionRecord>().SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken);
        providerConnection?.MarkChecked(refreshedAtUtc);
        await db.SaveChangesAsync(cancellationToken);
    }
}
