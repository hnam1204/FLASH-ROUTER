using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Reminders.Application;

public sealed class ReminderBoundary
{
    public Result RejectUntilSchedulerExists()
        => Result.Failure("REMINDERS_NOT_ENABLED", "The background scheduler is deferred to a later phase.");
}
