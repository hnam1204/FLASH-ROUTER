using System.Diagnostics;
using System.ComponentModel;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Extensions.Configuration;

namespace FlashAI.Modules.Connections.Application;

public sealed class ChromeExecutableResolver : IChromeExecutableResolver
{
    private readonly IConfiguration _configuration;

    public ChromeExecutableResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? Resolve()
    {
        var configured = _configuration["FlashAI:Browser:ChromeExecutable"];
        if (!string.IsNullOrWhiteSpace(configured) && IsExecutable(configured))
        {
            return Path.GetFullPath(configured);
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        return candidates.FirstOrDefault(IsExecutable);
    }

    private static bool IsExecutable(string path)
        => !string.IsNullOrWhiteSpace(path)
            && Path.GetFileName(path).Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path)
            && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}

public sealed class ChromeProcessLauncher : IChromeProcessLauncher
    , IChromeProcessLauncherDiagnostics, IDisposable
{
    private readonly IBrowserProfileOwnershipCoordinator _ownership;
    private readonly bool _ownsOwnership;

    public ChromeProcessLauncher(IBrowserProfileOwnershipCoordinator? ownership = null)
    {
        _ownership = ownership ?? new BrowserProfileOwnershipCoordinator();
        _ownsOwnership = ownership is null;
    }

    public bool TryLaunch(string executablePath, string profileDirectory, string officialUrl, out string? safeError)
    {
        var result = TryLaunchDetailed(executablePath, profileDirectory, officialUrl);
        safeError = result.IsSuccess ? null : result.Message;
        return result.IsSuccess;
    }

    public ChromeLaunchResult TryLaunchDetailed(string executablePath, string profileDirectory, string officialUrl)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return Failure("CHROME_NOT_FOUND", "Không tìm thấy Google Chrome. Vui lòng kiểm tra đường dẫn trình duyệt.");
        }

        if (string.IsNullOrWhiteSpace(profileDirectory) || !Directory.Exists(profileDirectory))
        {
            return Failure("INVALID_PROFILE_PATH", "Không thể truy cập thư mục Chrome profile được quản lý.");
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(profileDirectory).FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("PROFILE_PERMISSION_DENIED", "Không có quyền truy cập thư mục Chrome profile.");
        }
        catch (IOException)
        {
            return Failure("PROFILE_PERMISSION_DENIED", "Không thể truy cập thư mục Chrome profile.");
        }

        var ownership = _ownership.TryAcquire(profileDirectory, BrowserProfileOwnerKind.ManagedChrome);
        if (!ownership.IsAcquired || ownership.Lease is null)
        {
            return Failure(ownership.Status, ownership.Message);
        }

        var lease = ownership.Lease;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                WorkingDirectory = profileDirectory,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add($"--user-data-dir={profileDirectory}");
            startInfo.ArgumentList.Add("--new-window");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add("--disable-sync");
            startInfo.ArgumentList.Add(officialUrl);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                lease.Dispose();
                return Failure("BROWSER_EXITED_EARLY", "Chrome đã đóng ngay sau khi khởi động.");
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => lease.Dispose();
            if (process.HasExited)
            {
                lease.Dispose();
                return Failure("BROWSER_EXITED_EARLY", "Chrome đã đóng ngay sau khi khởi động.");
            }

            return new ChromeLaunchResult(true, "BROWSER_OPENED", "Đã mở Chrome bằng profile riêng của tài khoản.");
        }
        catch (Win32Exception exception)
        {
            lease.Dispose();
            return exception.NativeErrorCode is 2 or 3
                ? Failure("CHROME_NOT_FOUND", "Không tìm thấy Google Chrome. Vui lòng kiểm tra đường dẫn trình duyệt.")
                : Failure("UNKNOWN_BROWSER_FAILURE", "Windows không thể khởi chạy Chrome.");
        }
        catch (UnauthorizedAccessException)
        {
            lease.Dispose();
            return Failure("PROFILE_PERMISSION_DENIED", "Không có quyền khởi chạy Chrome hoặc truy cập profile.");
        }
        catch (IOException)
        {
            lease.Dispose();
            return Failure("PROFILE_PERMISSION_DENIED", "Không thể truy cập thư mục profile Chrome.");
        }
        catch (InvalidOperationException)
        {
            lease.Dispose();
            return Failure("BROWSER_EXITED_EARLY", "Chrome đã đóng ngay sau khi khởi động.");
        }
    }

    private static ChromeLaunchResult Failure(string status, string message)
        => new(false, status, message);

    public void Dispose()
    {
        if (_ownsOwnership && _ownership is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
