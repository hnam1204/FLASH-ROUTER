using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Playwright;

namespace FlashAI.Modules.Connections.Application;

/// <summary>
/// Resolves and installs the Chromium revision selected by this solution's
/// Microsoft.Playwright package. It never falls back to system Chrome.
/// </summary>
public sealed class PlaywrightBrowserRuntime : IPlaywrightBrowserRuntime
{
    private readonly string _packageVersion = typeof(Playwright).Assembly.GetName().Version?.ToString() ?? "unknown";
    private readonly string _browserCachePath;
    private readonly string? _expectedChromiumRevision;
    private readonly string _installerScript;

    public PlaywrightBrowserRuntime()
    {
        _browserCachePath = ResolveBrowserCachePath();
        _expectedChromiumRevision = ReadExpectedChromiumRevision();
        _installerScript = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
    }

    public async Task<PlaywrightBrowserRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
            var executablePath = playwright.Chromium.ExecutablePath;
            var resolvedRevision = ExtractRevision(executablePath);
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return Status(
                    PlaywrightChromiumReadiness.ChromiumInstallRequired,
                    executablePath,
                    resolvedRevision,
                    "Playwright đã được nạp nhưng Chromium tương thích chưa được cài. Hãy chọn Install/Repair Chromium.");
            }

            return Status(
                PlaywrightChromiumReadiness.ChromiumReady,
                executablePath,
                resolvedRevision,
                "Playwright-managed Chromium đã sẵn sàng.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            return Status(
                PlaywrightChromiumReadiness.ChromiumMissing,
                null,
                null,
                "Không thể nạp Playwright driver. Hãy build lại solution hoặc dùng Install/Repair Chromium.");
        }
        catch (IOException)
        {
            return Status(
                PlaywrightChromiumReadiness.ChromiumMissing,
                null,
                null,
                "Không thể đọc trạng thái Chromium-managed runtime.");
        }
    }

    public async Task<PlaywrightBrowserRuntimeStatus> InstallOrRepairAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_installerScript))
        {
            return Status(
                PlaywrightChromiumReadiness.ChromiumInstallFailed,
                null,
                null,
                "Không tìm thấy installer Playwright đi kèm build hiện tại.");
        }

        Process? process = null;
        try
        {
            process = Process.Start(CreateInstallerStartInfo());
            if (process is null)
            {
                return Status(
                    PlaywrightChromiumReadiness.ChromiumInstallFailed,
                    null,
                    null,
                    "Không thể khởi chạy installer Chromium.");
            }

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                return Status(
                    PlaywrightChromiumReadiness.ChromiumInstallFailed,
                    null,
                    null,
                    "Installer Chromium kết thúc không thành công. Kiểm tra quyền và kết nối rồi thử Repair lại.");
            }

            var status = await GetStatusAsync(cancellationToken);
            return status.IsReady
                ? status
                : status with
                {
                    Readiness = PlaywrightChromiumReadiness.ChromiumInstallFailed,
                    Message = "Installer đã chạy nhưng Playwright chưa resolve được Chromium tương thích."
                };
        }
        catch (OperationCanceledException)
        {
            TryStop(process);
            throw;
        }
        catch (Win32Exception)
        {
            return Status(
                PlaywrightChromiumReadiness.ChromiumInstallFailed,
                null,
                null,
                "Windows không thể khởi chạy installer Playwright.");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private ProcessStartInfo CreateInstallerStartInfo()
    {
        var shell = ResolvePowerShell();
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(_installerScript);
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("chromium");
        startInfo.Environment["PLAYWRIGHT_SKIP_BROWSER_GC"] = "1";
        return startInfo;
    }

    private PlaywrightBrowserRuntimeStatus Status(
        PlaywrightChromiumReadiness readiness,
        string? executablePath,
        string? resolvedRevision,
        string message)
        => new(
            readiness,
            _packageVersion,
            executablePath,
            _browserCachePath,
            _expectedChromiumRevision,
            resolvedRevision,
            message);

    private static string? ReadExpectedChromiumRevision()
    {
        var metadataPath = Path.Combine(AppContext.BaseDirectory, ".playwright", "package", "browsers.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            foreach (var browser in document.RootElement.GetProperty("browsers").EnumerateArray())
            {
                if (string.Equals(
                    browser.GetProperty("name").GetString(),
                    "chromium",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return browser.GetProperty("revision").GetString();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException)
        {
            // Metadata is diagnostic only; Playwright remains the source of executable resolution.
        }

        return null;
    }

    private static string ResolveBrowserCachePath()
    {
        var configured = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && !configured.Equals("0", StringComparison.Ordinal))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ms-playwright");
    }

    private static string? ExtractRevision(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetDirectoryName(executablePath)!);
        while (current is not null)
        {
            if (current.Name.StartsWith("chromium-", StringComparison.OrdinalIgnoreCase))
            {
                return current.Name["chromium-".Length..];
            }

            current = current.Parent;
        }

        return null;
    }

    private static string ResolvePowerShell()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            "pwsh.exe",
            "powershell.exe"
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void TryStop(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The owned installer already exited.
        }
    }
}
