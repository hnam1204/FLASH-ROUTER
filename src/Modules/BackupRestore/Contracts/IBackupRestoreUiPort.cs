using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.BackupRestore.Contracts;

public sealed record BackupItemVm(
    Guid BackupId,
    string Name,
    DateTimeOffset CreatedAt,
    string SizeDisplay,
    string Type,
    bool IsEncrypted,
    string Status);

public interface IBackupRestoreUiPort
{
    Task<IReadOnlyList<BackupItemVm>> GetBackupsAsync(CancellationToken cancellationToken = default);

    Task<OperationResultVm> RequestCreateBackupAsync(string name, string type, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RequestRestoreAsync(
        Guid backupId,
        bool confirmed = false,
        CancellationToken cancellationToken = default);
}
