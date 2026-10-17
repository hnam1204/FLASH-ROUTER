namespace FlashAI.Modules.Reminders.Contracts;

public sealed record ReminderVm(
    Guid ReminderId,
    Guid AccountId,
    string Title,
    DateTimeOffset DueDate,
    string Type,
    bool IsCompleted,
    string Status);
