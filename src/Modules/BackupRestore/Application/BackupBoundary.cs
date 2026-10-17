using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.BackupRestore.Application;

public sealed class BackupBoundary
{
    public static Result RejectUntilConsistentSnapshotExists()
        => Result.Failure("BACKUP_NOT_ENABLED", "Legacy boundary retained; use the active encrypted SQLite backup flow.");
}
