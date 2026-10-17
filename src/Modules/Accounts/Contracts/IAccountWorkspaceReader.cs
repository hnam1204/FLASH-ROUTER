namespace FlashAI.Modules.Accounts.Contracts;

public interface IAccountWorkspaceReader
{
    Task<IReadOnlyList<AccountListItemVm>> GetAccountsAsync(
        string? search = null,
        string? filter = null,
        string? sort = null,
        CancellationToken cancellationToken = default);

    Task<AccountDetailVm?> GetAccountDetailAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
