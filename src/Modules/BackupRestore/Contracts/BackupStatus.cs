namespace FlashAI.Modules.BackupRestore.Contracts;

public sealed record BackupStatus(bool IsAvailable, string Phase, string? Message);
