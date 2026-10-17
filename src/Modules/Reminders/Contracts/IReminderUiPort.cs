using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Reminders.Contracts;

public interface IReminderUiPort
{
    Task<IReadOnlyList<ReminderVm>> GetRemindersAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> CreateReminderAsync(Guid accountId, string title, DateTimeOffset dueDate, string type, CancellationToken cancellationToken = default);

    Task<OperationResultVm> ToggleCompleteAsync(Guid reminderId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken = default);
}
