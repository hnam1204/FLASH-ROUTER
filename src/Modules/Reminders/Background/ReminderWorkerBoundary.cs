namespace FlashAI.Modules.Reminders.Background;

public sealed class ReminderWorkerBoundary
{
    public bool IsRunning => false;

    public bool SupportsCancellation => true;
}
