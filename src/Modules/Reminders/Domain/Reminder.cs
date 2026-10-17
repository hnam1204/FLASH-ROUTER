namespace FlashAI.Modules.Reminders.Domain;

public sealed record Reminder(Guid Id, Guid AccountId, string Title, DateTimeOffset DueAt, bool IsResolved);
