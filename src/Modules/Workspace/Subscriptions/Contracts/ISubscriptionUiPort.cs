using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Subscriptions.Contracts;

public interface ISubscriptionUiPort
{
    Task<SubscriptionObservationVm?> GetSubscriptionAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RecordManualObservationAsync(
        Guid accountId,
        string plan,
        DateTimeOffset? expiryTrackingDate,
        string? notes,
        CancellationToken cancellationToken = default);
}
