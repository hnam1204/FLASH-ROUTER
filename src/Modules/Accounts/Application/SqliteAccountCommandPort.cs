using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Accounts.Application;

public sealed class SqliteAccountCommandPort : IAccountCommandPort
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly IClock _clock;

    public SqliteAccountCommandPort(IDbContextFactory<FlashAiDbContext> contextFactory, IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<OperationResultVm> CreateAccountAsync(AccountEditorVm model, CancellationToken cancellationToken = default)
    {
        var validation = AccountInputValidator.Validate(model, null, out var input);
        return validation ?? await RunCommandAsync(() => CreateCoreAsync(input!, cancellationToken), cancellationToken);
    }

    public async Task<OperationResultVm> UpdateAccountAsync(Guid accountId, AccountEditorVm model, CancellationToken cancellationToken = default)
    {
        var validation = AccountInputValidator.Validate(model, accountId, out var input);
        return validation ?? await RunCommandAsync(() => UpdateCoreAsync(accountId, model, input!, cancellationToken), cancellationToken);
    }

    public Task<OperationResultVm> ToggleFavoriteAsync(Guid accountId, CancellationToken cancellationToken = default)
        => RunCommandAsync(() => ToggleFavoriteCoreAsync(accountId, cancellationToken), cancellationToken);

    public Task<OperationResultVm> ArchiveAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => RunCommandAsync(() => ArchiveCoreAsync(accountId, cancellationToken), cancellationToken);

    public Task<OperationResultVm> RestoreAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => RunCommandAsync(() => RestoreCoreAsync(accountId, cancellationToken), cancellationToken);

    public Task<OperationResultVm> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => RunCommandAsync(() => DeleteCoreAsync(accountId, cancellationToken), cancellationToken);

    private Task<OperationResultVm> RunCommandAsync(
        Func<Task<OperationResultVm>> operation,
        CancellationToken cancellationToken)
        => SqliteRetryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException)
            {
                return OperationResultVm.Failure("CONFLICT", "Bản ghi đã thay đổi. Hãy tải lại trước khi lưu.");
            }
            catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
            {
                return OperationResultVm.Failure(
                    "DUPLICATE",
                    "Email này đã tồn tại với cùng nhà cung cấp.",
                    new Dictionary<string, string[]> { ["Email"] = ["Email đã tồn tại với nhà cung cấp này."] });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return OperationResultVm.Failure("CANCELLED", "Thao tác đã bị hủy.");
            }
            catch (Exception)
            {
                return OperationResultVm.Failure("PERSISTENCE_ERROR", "Không thể hoàn tất thao tác với cơ sở dữ liệu cục bộ.");
            }
        }, cancellationToken);

    private async Task<OperationResultVm> CreateCoreAsync(
        ValidatedAccountInput input,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var duplicate = await context.Set<AccountRecord>().AnyAsync(
            x => x.NormalizedEmail == input.NormalizedEmail && x.NormalizedProvider == input.NormalizedProvider,
            cancellationToken);
        if (duplicate)
        {
            return OperationResultVm.Failure(
                "DUPLICATE",
                "Email này đã tồn tại với cùng nhà cung cấp.",
                new Dictionary<string, string[]> { ["Email"] = ["Email đã tồn tại với nhà cung cấp này."] });
        }

        var nextNumericId = (await context.Set<AccountRecord>()
            .Select(x => (int?)x.NumericId)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var now = _clock.UtcNow;
        var account = new AccountRecord(
            Guid.NewGuid(),
            nextNumericId,
            input.Email,
            input.NormalizedEmail,
            input.DisplayName,
            input.NormalizedDisplayName,
            input.Notes,
            input.NormalizedNotes,
            input.Provider,
            input.NormalizedProvider,
            false,
            input.Status,
            input.RecordedPlan,
            input.NormalizedRecordedPlan,
            now,
            "Người dùng",
            now,
            now,
            null,
            "Người dùng");

        foreach (var tag in input.Tags)
        {
            account.Tags.Add(new AccountTagRecord(account.Id, tag, AccountInputValidator.NormalizeForSearch(tag)));
        }

        context.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResultVm.Success("Đã tạo tài khoản và lưu vào SQLite.");
    }

    private async Task<OperationResultVm> UpdateCoreAsync(
        Guid accountId,
        AccountEditorVm model,
        ValidatedAccountInput input,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var account = await context.Set<AccountRecord>()
            .Include(x => x.Tags)
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        var originalVersion = account.Version;
        if (model.Version.HasValue && model.Version.Value != originalVersion)
        {
            return OperationResultVm.Failure("CONFLICT", "Bản ghi đã thay đổi. Hãy tải lại trước khi lưu.");
        }

        var duplicate = await context.Set<AccountRecord>().AnyAsync(
            x => x.Id != accountId &&
                 x.NormalizedEmail == input.NormalizedEmail &&
                 x.NormalizedProvider == input.NormalizedProvider,
            cancellationToken);
        if (duplicate)
        {
            return OperationResultVm.Failure(
                "DUPLICATE",
                "Email này đã tồn tại với cùng nhà cung cấp.",
                new Dictionary<string, string[]> { ["Email"] = ["Email đã tồn tại với nhà cung cấp này."] });
        }

        account.UpdateProfile(
            input.Email,
            input.NormalizedEmail,
            input.DisplayName,
            input.NormalizedDisplayName,
            input.Notes,
            input.NormalizedNotes,
            input.Provider,
            input.NormalizedProvider,
            input.Status,
            input.RecordedPlan,
            input.NormalizedRecordedPlan,
            _clock.UtcNow);
        context.RemoveRange(account.Tags);
        account.Tags.Clear();
        foreach (var tag in input.Tags)
        {
            account.Tags.Add(new AccountTagRecord(account.Id, tag, AccountInputValidator.NormalizeForSearch(tag)));
        }

        context.Entry(account).Property(x => x.Version).OriginalValue = originalVersion;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResultVm.Success("Đã lưu thay đổi vào SQLite.");
    }

    private async Task<OperationResultVm> ToggleFavoriteCoreAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>().SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        var originalVersion = account.Version;
        account.SetFavorite(!account.IsFavorite, _clock.UtcNow);
        context.Entry(account).Property(x => x.Version).OriginalValue = originalVersion;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResultVm.Success();
    }

    private async Task<OperationResultVm> ArchiveCoreAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>().SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        if (account.IsArchived)
        {
            return OperationResultVm.Failure("ALREADY_ARCHIVED", "Tài khoản đã được lưu trữ.");
        }

        var originalVersion = account.Version;
        account.Archive(_clock.UtcNow);
        context.Entry(account).Property(x => x.Version).OriginalValue = originalVersion;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResultVm.Success("Đã lưu trữ tài khoản trong SQLite.");
    }

    private async Task<OperationResultVm> RestoreCoreAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>().SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        if (!account.IsArchived)
        {
            return OperationResultVm.Failure("ALREADY_ACTIVE", "Tài khoản chưa được lưu trữ.");
        }

        var originalVersion = account.Version;
        account.Restore(_clock.UtcNow);
        context.Entry(account).Property(x => x.Version).OriginalValue = originalVersion;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResultVm.Success("Đã khôi phục tài khoản trong SQLite.");
    }

    private async Task<OperationResultVm> DeleteCoreAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>().SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        context.Remove(account);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResultVm.Success("Đã xóa tài khoản khỏi SQLite.");
    }

    private static bool IsUniqueConstraint(DbUpdateException exception)
    {
        var current = exception.InnerException;
        while (current is not null)
        {
            if (current is SqliteException { SqliteErrorCode: 19 })
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
