using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Accounts.Contracts;

public interface IPermanentAccountDeletionService
{
    Task<OperationResultVm> DeletePermanentlyAsync(
        Guid accountId,
        string confirmedEmail,
        CancellationToken cancellationToken = default);
}
