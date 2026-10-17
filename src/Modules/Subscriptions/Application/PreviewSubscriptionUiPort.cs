using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Subscriptions.Contracts;

namespace FlashAI.Modules.Subscriptions.Application;

public sealed class PreviewSubscriptionUiPort : ISubscriptionUiPort
{
    private readonly Dictionary<Guid, SubscriptionObservationVm> _observations = [];

    public Task<SubscriptionObservationVm?> GetSubscriptionAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (!_observations.TryGetValue(accountId, out var vm))
        {
            vm = new SubscriptionObservationVm(
                accountId,
                "Plus",
                "Đã ghi nhận",
                new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.FromHours(7)),
                new DateTimeOffset(2026, 10, 15, 9, 12, 0, TimeSpan.FromHours(7)),
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(7)),
                "Gói đăng ký ChatGPT Plus theo dõi đến hạn ngày 15/10/2026.",
                [
                    new(new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.FromHours(7)), "Plus", "Người dùng ghi nhận", "Bắt đầu chu kỳ gia hạn"),
                    new(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(7)), "Plus", "Người dùng ghi nhận", "Chu kỳ trước đó")
                ]
            );
            _observations[accountId] = vm;
        }

        return Task.FromResult<SubscriptionObservationVm?>(vm);
    }

    public Task<OperationResultVm> RecordManualObservationAsync(
        Guid accountId,
        string plan,
        DateTimeOffset? expiryTrackingDate,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var existing = _observations.TryGetValue(accountId, out var current) ? current : null;
        var history = existing?.History.ToList() ?? [];

        history.Insert(0, new SubscriptionObservationHistoryItemVm(
            DateTimeOffset.Now,
            plan,
            "Người dùng cập nhật",
            notes));

        _observations[accountId] = new SubscriptionObservationVm(
            accountId,
            plan,
            "Người dùng",
            DateTimeOffset.Now,
            null,
            expiryTrackingDate,
            notes,
            history
        );

        return Task.FromResult(OperationResultVm.Success("Đã lưu thông tin gói vào bộ nhớ xem trước."));
    }
}
