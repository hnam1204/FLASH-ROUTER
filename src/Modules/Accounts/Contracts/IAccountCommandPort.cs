using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Accounts.Contracts;

public interface IAccountCommandPort
{
    Task<OperationResultVm> CreateAccountAsync(AccountEditorVm model, CancellationToken cancellationToken = default);

    Task<OperationResultVm> UpdateAccountAsync(Guid accountId, AccountEditorVm model, CancellationToken cancellationToken = default);

    Task<OperationResultVm> ToggleFavoriteAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> ArchiveAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RestoreAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

}
