using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed class BrowserProfileManager : IBrowserProfileManager, IDisposable
{
    private const string BrowserKind = "Chrome";
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly LocalDataPaths _paths;
    private readonly IChromeExecutableResolver _executableResolver;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BrowserProfileManager(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        LocalDataPaths paths,
        IChromeExecutableResolver executableResolver)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _executableResolver = executableResolver;
    }

    public async Task<BrowserProfileBindingVm?> GetBindingAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || !IsAllowedProvider(providerId))
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var binding = await context.Set<BrowserProfileBindingRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == accountId
                && item.ProviderId == providerId
                && item.BrowserKind == BrowserKind
                && item.State != BrowserProfileState.Archived.ToString(), cancellationToken);
        return binding is null ? null : ToViewModel(binding);
    }

    public async Task<BrowserProfilePreparationResult> PrepareAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return Failure("ACCOUNT_REQUIRED", "Không xác định được tài khoản cần mở Chrome.");
        }

        if (!IsAllowedProvider(providerId))
        {
            return Failure("BROWSER_PROVIDER_NOT_ALLOWED", "Chỉ cho phép mở profile của provider chính thức đã được cấu hình.");
        }

        var executable = _executableResolver.Resolve();
        if (executable is null)
        {
            return Failure("CHROME_NOT_FOUND", "Không tìm thấy Google Chrome. Không có profile nào được mở.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await AccountExistsAsync(context, accountId, cancellationToken))
            {
                return Failure("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại hoặc đã được lưu trữ.");
            }

            var binding = await context.Set<BrowserProfileBindingRecord>()
                .SingleOrDefaultAsync(item => item.AccountId == accountId
                    && item.ProviderId == providerId
                    && item.BrowserKind == BrowserKind
                    && item.State != BrowserProfileState.Archived.ToString(), cancellationToken);
            if (binding is null)
            {
                binding = await CreateBindingAsync(context, accountId, providerId, cancellationToken);
            }

            if (!IsManagedDirectory(binding.ProfileDirectoryReference))
            {
                return Failure("BROWSER_PROFILE_PATH_INVALID", "Thư mục Chrome của tài khoản không thuộc vùng quản lý an toàn.");
            }

            Directory.CreateDirectory(binding.ProfileDirectoryReference);
            var bindingVm = ToViewModel(binding);
            return new BrowserProfilePreparationResult(
                true,
                "BrowserProfileReady",
                "Profile Chrome riêng của tài khoản đã sẵn sàng.",
                new BrowserProfileSessionContext(
                    binding.Id,
                    binding.AccountId,
                    binding.ProviderId,
                    binding.OpaqueProfileIdentifier,
                    binding.ProfileDirectoryReference,
                    executable,
                    bindingVm));
        }
        catch (DbUpdateException)
        {
            return Failure("BROWSER_PROFILE_SAVE_FAILED", "Không thể lưu liên kết profile Chrome vào SQLite.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("BROWSER_PROFILE_PERMISSION_DENIED", "Không có quyền truy cập profile Chrome được quản lý.");
        }
        catch (IOException)
        {
            return Failure("BROWSER_PROFILE_DIRECTORY_FAILED", "Không thể tạo thư mục profile Chrome an toàn.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordLaunchResultAsync(
        Guid bindingId,
        bool succeeded,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var binding = await context.Set<BrowserProfileBindingRecord>()
            .SingleOrDefaultAsync(item => item.Id == bindingId, cancellationToken);
        if (binding is null)
        {
            return;
        }

        if (succeeded)
        {
            binding.MarkOpened(DateTimeOffset.UtcNow);
        }
        else
        {
            binding.MarkLaunchFailed(DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<BrowserProfileBindingRecord> CreateBindingAsync(
        FlashAiDbContext context,
        Guid accountId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var profileId = Guid.NewGuid().ToString("N");
        var root = GetProfileRoot();
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, profileId);
        var binding = new BrowserProfileBindingRecord(
            Guid.NewGuid(),
            accountId,
            providerId,
            BrowserKind,
            profileId,
            directory,
            DateTimeOffset.UtcNow);
        context.Add(binding);
        await context.SaveChangesAsync(cancellationToken);
        return binding;
    }

    private string GetProfileRoot() => Path.GetFullPath(Path.Combine(_paths.Root, ".local", "browser-profiles"));

    private bool IsManagedDirectory(string directory)
    {
        var root = GetProfileRoot();
        var fullDirectory = Path.GetFullPath(directory);
        var comparison = StringComparison.OrdinalIgnoreCase;
        return fullDirectory.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            && !HasReparsePointBetween(root, fullDirectory);
    }

    private static bool HasReparsePointBetween(string root, string directory)
    {
        var current = directory;
        while (current.Length >= root.Length)
        {
            if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (current.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Directory.GetParent(current)?.FullName ?? root;
        }

        return false;
    }

    private static async Task<bool> AccountExistsAsync(
        FlashAiDbContext context,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM AccountProfiles WHERE Id = $accountId AND IsArchived = 0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$accountId";
        parameter.Value = accountId;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count && count > 0;
    }

    private static bool IsAllowedProvider(string providerId)
        => providerId.Equals("openai-chatgpt", StringComparison.OrdinalIgnoreCase)
            || providerId.Equals("openai", StringComparison.OrdinalIgnoreCase);

    private static BrowserProfileBindingVm ToViewModel(BrowserProfileBindingRecord binding)
        => new(binding.Id, binding.AccountId, binding.ProviderId, binding.BrowserKind, binding.OpaqueProfileIdentifier, binding.State, binding.CreatedAtUtc, binding.LastOpenedAtUtc);

    private static BrowserProfilePreparationResult Failure(string status, string message)
        => new(false, status, message);

    public void Dispose() => _gate.Dispose();
}
