using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed class BrowserProfileUiPort : IBrowserProfileUiPort, IDisposable
{
    private static string ChatGptUrl => BrowserOriginPolicy.ChatGptUri.ToString();
    private readonly IBrowserProfileManager _profileManager;
    private readonly IChromeProcessLauncher _processLauncher;
    private readonly bool _ownsProfileManager;

    public BrowserProfileUiPort(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        LocalDataPaths paths,
        IChromeExecutableResolver executableResolver,
        IChromeProcessLauncher processLauncher)
        : this(new BrowserProfileManager(contextFactory, paths, executableResolver), processLauncher, ownsProfileManager: true)
    {
    }

    public BrowserProfileUiPort(
        IBrowserProfileManager profileManager,
        IChromeProcessLauncher processLauncher)
        : this(profileManager, processLauncher, ownsProfileManager: false)
    {
    }

    private BrowserProfileUiPort(
        IBrowserProfileManager profileManager,
        IChromeProcessLauncher processLauncher,
        bool ownsProfileManager)
    {
        _profileManager = profileManager;
        _processLauncher = processLauncher;
        _ownsProfileManager = ownsProfileManager;
    }

    public Task<BrowserProfileBindingVm?> GetBindingAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default)
        => _profileManager.GetBindingAsync(accountId, providerId, cancellationToken);

    public async Task<BrowserLaunchResult> OpenAsync(
        Guid accountId,
        BrowserOpenTarget target,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default)
    {
        var prepared = await _profileManager.PrepareAsync(accountId, providerId, cancellationToken);
        if (!prepared.IsSuccess || prepared.Context is null)
        {
            return new BrowserLaunchResult(false, prepared.Status, prepared.Message);
        }

        var context = prepared.Context;
        var launch = _processLauncher is IChromeProcessLauncherDiagnostics diagnostics
            ? diagnostics.TryLaunchDetailed(context.ChromeExecutablePath, context.ProfileDirectory, ChatGptUrl)
            : TryLaunchLegacy(context);
        if (!launch.IsSuccess)
        {
            await _profileManager.RecordLaunchResultAsync(context.BindingId, false, cancellationToken);
            var binding = await _profileManager.GetBindingAsync(accountId, providerId, cancellationToken);
            return new BrowserLaunchResult(false, launch.Status, launch.Message, binding);
        }

        await _profileManager.RecordLaunchResultAsync(context.BindingId, true, cancellationToken);
        var openedBinding = await _profileManager.GetBindingAsync(accountId, providerId, cancellationToken);
        var message = target == BrowserOpenTarget.OfficialUsage
            ? "Đã mở Chrome bằng profile riêng của tài khoản. Usage web chính thức có thể được xem trong phiên này; hệ thống không tự xác minh session."
            : "Đã mở Chrome bằng profile riêng của tài khoản. Việc mở trình duyệt không đồng nghĩa đã đăng nhập.";
        return new BrowserLaunchResult(true, target == BrowserOpenTarget.OfficialUsage ? "UsagePageOpened" : "BrowserOpened", message, openedBinding);
    }

    private ChromeLaunchResult TryLaunchLegacy(BrowserProfileSessionContext context)
        => _processLauncher.TryLaunch(context.ChromeExecutablePath, context.ProfileDirectory, ChatGptUrl, out var error)
            ? new ChromeLaunchResult(true, "BROWSER_OPENED", "Đã mở Chrome bằng profile riêng của tài khoản.")
            : new ChromeLaunchResult(false, "BrowserLaunchFailed", error ?? "Không thể mở Chrome.");

    public void Dispose()
    {
        if (_ownsProfileManager && _profileManager is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public sealed class PreviewBrowserProfileUiPort : IBrowserProfileUiPort
{
    public Task<BrowserProfileBindingVm?> GetBindingAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<BrowserProfileBindingVm?>(null);
    }

    public Task<BrowserLaunchResult> OpenAsync(Guid accountId, BrowserOpenTarget target, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BrowserLaunchResult(
            false,
            "PREVIEW_BROWSER_DISABLED",
            "Preview mode không tạo Chrome profile thật. Chuyển sang Integrated mode để dùng profile riêng theo tài khoản."));
    }
}
