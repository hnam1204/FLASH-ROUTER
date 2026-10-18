using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Concurrent;
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
    , IChromeProcessLauncherDiagnostics, IChromeProcessController, IDisposable
{
    private readonly IBrowserProfileOwnershipCoordinator _ownership;
    private readonly bool _ownsOwnership;
    private readonly ConcurrentDictionary<string, TrackedProcess> _processes = new(StringComparer.OrdinalIgnoreCase);

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

            var process = Process.Start(startInfo);
            if (process is null)
            {
                lease.Dispose();
                return Failure("BROWSER_EXITED_EARLY", "Chrome đã đóng ngay sau khi khởi động.");
            }

            process.EnableRaisingEvents = true;
            var canonicalPath = Path.GetFullPath(profileDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var tracked = new TrackedProcess(canonicalPath, process, lease);
            if (!_processes.TryAdd(canonicalPath, tracked))
            {
                process.Dispose();
                lease.Dispose();
                return Failure("PROFILE_IN_USE", "Profile Chrome đang được sử dụng bởi một phiên đã được quản lý.");
            }

            process.Exited += (_, _) => ReleaseTracked(canonicalPath, process);
            if (process.HasExited)
            {
                ReleaseTracked(canonicalPath, process);
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

    public bool IsRunning(string profileDirectory)
    {
        var key = Canonicalize(profileDirectory);
        return key is not null
            && _processes.TryGetValue(key, out var tracked)
            && !tracked.Process.HasExited;
    }

    public bool TryClose(string profileDirectory, out string? safeError)
    {
        safeError = null;
        var key = Canonicalize(profileDirectory);
        if (key is null || !_processes.TryGetValue(key, out var tracked))
        {
            return true;
        }

        try
        {
            if (tracked.Process.HasExited)
            {
                ReleaseTracked(key, tracked.Process);
                return true;
            }

            if (!tracked.Process.CloseMainWindow())
            {
                safeError = "Không thể gửi yêu cầu đóng tới cửa sổ Chrome thuộc profile này.";
                return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            ReleaseTracked(key, tracked.Process);
            return true;
        }
        catch (Win32Exception)
        {
            safeError = "Windows không thể gửi yêu cầu đóng tới Chrome thuộc profile này.";
            return false;
        }
    }

    private void ReleaseTracked(string canonicalPath, Process process)
    {
        if (_processes.TryRemove(canonicalPath, out var removed))
        {
            removed.Lease.Dispose();
            removed.Process.Dispose();
        }
    }

    private static string? Canonicalize(string? profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(profileDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record TrackedProcess(string ProfileDirectory, Process Process, IDisposable Lease);

    private static ChromeLaunchResult Failure(string status, string message)
        => new(false, status, message);

    public void Dispose()
    {
        foreach (var pair in _processes.ToArray())
        {
            ReleaseTracked(pair.Key, pair.Value.Process);
        }

        if (_ownsOwnership && _ownership is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
