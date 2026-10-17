namespace FlashAI.Modules.Accounts.Contracts;

public interface IAccountCatalog
{
    ValueTask<bool> ExistsAsync(Guid accountId, CancellationToken cancellationToken = default);

    ValueTask<AccountSummary?> FindAsync(Guid accountId, CancellationToken cancellationToken = default);
}
