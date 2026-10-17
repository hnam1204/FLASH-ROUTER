using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Domain;
using FlashAI.Modules.Accounts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;

public sealed class SqliteAccountWorkspaceReader : IAccountWorkspaceReader, IAccountCatalog
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteAccountWorkspaceReader(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public Task<IReadOnlyList<AccountListItemVm>> GetAccountsAsync(
        string? search = null,
        string? filter = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
        => SqliteRetryPolicy.ExecuteAsync(
            () => GetAccountsCoreAsync(search, filter, sort, cancellationToken),
            cancellationToken);

    public Task<AccountDetailVm?> GetAccountDetailAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
        => SqliteRetryPolicy.ExecuteAsync(
            () => GetAccountDetailCoreAsync(accountId, cancellationToken),
            cancellationToken);

    public async ValueTask<bool> ExistsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<AccountRecord>().AsNoTracking().AnyAsync(x => x.Id == accountId, cancellationToken);
    }

    public async ValueTask<AccountSummary?> FindAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>()
            .AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => new AccountSummary(x.Id, x.DisplayName, x.IsArchived))
            .SingleOrDefaultAsync(cancellationToken);
        return account;
    }

    private async Task<IReadOnlyList<AccountListItemVm>> GetAccountsCoreAsync(
        string? search,
        string? filter,
        string? sort,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<AccountRecord>().AsNoTracking().AsQueryable();
        var normalizedFilter = filter?.Trim().ToLowerInvariant();

        if (normalizedFilter == "archived")
        {
            query = query.Where(x => x.IsArchived);
        }
        else
        {
            query = query.Where(x => !x.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = AccountInputValidator.NormalizeForSearch(search);
            query = query.Where(x =>
                x.NormalizedEmail.Contains(term) ||
                x.NormalizedDisplayName.Contains(term) ||
                (x.NormalizedNotes != null && x.NormalizedNotes.Contains(term)) ||
                x.NormalizedProvider.Contains(term) ||
                x.NormalizedRecordedPlan.Contains(term) ||
                x.Tags.Any(tag => tag.NormalizedValue.Contains(term)));
        }

        query = normalizedFilter switch
        {
            "connected" => query.Where(x => x.ConnectionStatus == "Connected"),
            "unverified" => query.Where(x => x.ConnectionStatus == "Unverified"),
            "attention" => query.Where(x => x.ConnectionStatus == "ReauthNeeded" || x.ConnectionStatus == "ConnectionError"),
            "favorite" => query.Where(x => x.IsFavorite),
            _ => query
        };

        query = sort?.Trim().ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(x => x.TrackingDateTicksUtc).ThenBy(x => x.NumericId),
            "name_asc" => query.OrderBy(x => x.NormalizedDisplayName).ThenBy(x => x.NumericId),
            "name_desc" => query.OrderByDescending(x => x.NormalizedDisplayName).ThenByDescending(x => x.NumericId),
            "plan" => query.OrderBy(x => x.NormalizedRecordedPlan).ThenBy(x => x.NumericId),
            _ => query.OrderByDescending(x => x.TrackingDateTicksUtc).ThenByDescending(x => x.NumericId)
        };

        var records = await query.Include(x => x.Tags).ToListAsync(cancellationToken);
        return records.Select(AccountProjectionMapper.ToListItem).ToArray();
    }

    private async Task<AccountDetailVm?> GetAccountDetailCoreAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>()
            .AsNoTracking()
            .Include(x => x.Tags)
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        return account is null ? null : AccountProjectionMapper.ToDetail(account);
    }
}
