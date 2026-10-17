using FlashAI.Foundation.Contracts;
using FlashAI.Modules.BackupRestore.Contracts;

namespace FlashAI.Modules.BackupRestore.Application;

public sealed class PreviewBackupRestoreUiPort : IBackupRestoreUiPort
{
    private readonly List<BackupItemVm> _backups = [];

    public PreviewBackupRestoreUiPort()
    {
        _backups.AddRange([
            new BackupItemVm(
                Guid.NewGuid(),
                "flash-backup-preview-demo.bak",
                DateTimeOffset.Now.AddDays(-3),
                "142 KB",
                "Toàn phần",
                true,
                "Khả dụng"
            )
        ]);
    }

    public Task<IReadOnlyList<BackupItemVm>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<BackupItemVm>>(_backups);
    }

    public Task<OperationResultVm> RequestCreateBackupAsync(string name, string type, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResultVm.Failure(
            "BACKEND_PENDING",
            "Tính năng tạo bản sao lưu vật lý đang chờ cấu hình SQLite Persistence và DPAPI Key Provider từ Backend."));
    }

    public Task<OperationResultVm> RequestRestoreAsync(Guid backupId, bool confirmed = false, CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            return Task.FromResult(OperationResultVm.Failure(
                "RESTORE_CONFIRMATION_REQUIRED",
                "Khôi phục cần xác nhận rõ ràng và chỉ được thực hiện trên dữ liệu tạm trong BE-06."));
        }

        return Task.FromResult(OperationResultVm.Failure(
            "BACKEND_PENDING",
            "Tính năng khôi phục dữ liệu vật lý đang chờ Backend triển khai ở Phase tiếp theo."));
    }
}
