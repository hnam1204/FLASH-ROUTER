namespace FlashAI.Modules.Reminders.Contracts;

public interface IReminderScheduler
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
