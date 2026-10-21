using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class EphemeralBrowserProfileFactory : IEphemeralBrowserProfileFactory
{
    private readonly LocalDataPaths _paths;
    private readonly IPlaywrightBrowserRuntime _browserRuntime;

    public EphemeralBrowserProfileFactory(
        LocalDataPaths paths,
        IPlaywrightBrowserRuntime browserRuntime)
    {
        _paths = paths;
        _browserRuntime = browserRuntime;
    }

    public async Task<EphemeralBrowserProfilePreparationResult> PrepareAsync(
        BrowserSessionMode sessionMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = await _browserRuntime.GetStatusAsync(cancellationToken);
        if (!runtime.IsReady)
        {
            return new EphemeralBrowserProfilePreparationResult(
                false,
                runtime.StatusCode,
                runtime.Message);
        }

        var profileId = Guid.NewGuid().ToString("N");
        var root = sessionMode == BrowserSessionMode.KeepBrowserSession
            ? _paths.AutomationBrowserProfiles
            : Path.Combine(_paths.AutomationBrowserProfiles, ".temp");
        var directory = Path.GetFullPath(Path.Combine(root, profileId));
        Directory.CreateDirectory(directory);

        var bindingId = Guid.NewGuid();
        var binding = new BrowserProfileBindingVm(
            bindingId,
            Guid.Empty,
            "openai-chatgpt",
            "Chrome",
            profileId,
            "Available",
            DateTimeOffset.UtcNow,
            null);
        var context = new BrowserProfileSessionContext(
            bindingId,
            Guid.Empty,
            "openai-chatgpt",
            profileId,
            directory,
            runtime.ExecutablePath ?? string.Empty,
            binding);

        // The orchestrator retains this exact directory only for a bounded
        // manual-continuation candidate or an approved promotion. All other
        // terminal paths clean up only this generated profile directory.
        Func<ValueTask> cleanup = () => DeleteTemporaryProfileAsync(directory);
        return new EphemeralBrowserProfilePreparationResult(
            true,
            "EphemeralBrowserProfileReady",
            sessionMode == BrowserSessionMode.Temporary
                ? "Đã tạo Chrome profile tạm cho attempt hiện tại."
                : "Đã tạo Chrome session profile để giữ lại cho lần mở sau.",
            context,
            cleanup);
    }

    private static ValueTask DeleteTemporaryProfileAsync(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // The browser may still be closing; the bounded temp root remains recoverable.
        }
        catch (UnauthorizedAccessException)
        {
            // Never broaden cleanup beyond the exact generated profile directory.
        }

        return ValueTask.CompletedTask;
    }
}
