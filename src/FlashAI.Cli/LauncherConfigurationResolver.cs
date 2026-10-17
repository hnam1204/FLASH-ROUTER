using System.Text.Json;

namespace FlashAI.Cli;

public static class LauncherConfigurationResolver
{
    public static LauncherRuntimeOptions Resolve()
    {
        var installationRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var settings = ReadSettings(Path.Combine(installationRoot, "flashrouter.settings.json"));
        var workspaceRoot = FindWorkspaceRoot(installationRoot);
        var dataRoot = ResolveDataRoot(settings, workspaceRoot);
        var endpoint = ResolveEndpoint(settings);
        var host = ResolveHost(settings, workspaceRoot, installationRoot);
        var browser = ResolveBrowser(settings);
        var timeout = settings.StartupTimeoutSeconds is >= 5 and <= 120
            ? settings.StartupTimeoutSeconds.Value
            : 30;

        return new LauncherRuntimeOptions(
            installationRoot,
            dataRoot,
            endpoint,
            host.Path,
            host.IsExecutable,
            browser,
            timeout,
            Path.Combine(dataRoot, ".local", "logs", "launcher.log"));
    }

    public static string? FindWorkspaceRoot(string startingPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startingPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static LauncherSettings ReadSettings(string path)
    {
        if (!File.Exists(path))
        {
            return new LauncherSettings(null, null, null, null, null);
        }

        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonDefaults.Web)
                ?? new LauncherSettings(null, null, null, null, null);
        }
        catch (JsonException exception)
        {
            throw new LauncherConfigurationException("CONFIG_INVALID", "flashrouter.settings.json is invalid.", exception);
        }
        catch (IOException exception)
        {
            throw new LauncherConfigurationException("CONFIG_UNAVAILABLE", "The launcher settings file could not be read.", exception);
        }
    }

    private static string ResolveDataRoot(LauncherSettings settings, string? workspaceRoot)
    {
        var configured = Environment.GetEnvironmentVariable("FLASHROUTER_DATA_ROOT")
            ?? settings.DataRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        }

        if (workspaceRoot is not null)
        {
            return workspaceRoot;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new LauncherConfigurationException("DATA_ROOT_UNAVAILABLE", "The per-user local data directory is unavailable.");
        }

        return Path.Combine(localAppData, "FlashRouter");
    }

    private static Uri ResolveEndpoint(LauncherSettings settings)
    {
        var value = Environment.GetEnvironmentVariable("FLASHROUTER_ENDPOINT")
            ?? settings.Endpoint
            ?? "http://127.0.0.1:5270";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || endpoint.Host is not ("localhost" or "127.0.0.1" or "::1"))
        {
            throw new LauncherConfigurationException("ENDPOINT_INVALID", "The launcher endpoint must use localhost or 127.0.0.1.");
        }

        return endpoint;
    }

    private static (string Path, bool IsExecutable) ResolveHost(
        LauncherSettings settings,
        string? workspaceRoot,
        string installationRoot)
    {
        var configured = Environment.GetEnvironmentVariable("FLASHROUTER_HOST_PATH")
            ?? settings.HostPath;
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(Environment.ExpandEnvironmentVariables(configured));
        }

        candidates.Add(Path.Combine(installationRoot, "FLASH_ROUTER.exe"));
        candidates.Add(Path.Combine(installationRoot, "FLASH_ROUTER.dll"));

        if (workspaceRoot is not null)
        {
            var hostRoot = Path.Combine(workspaceRoot, "FLASH ROUTER");
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                candidates.Add(Path.Combine(hostRoot, "bin", configuration, "net8.0", "win-x64", "FLASH_ROUTER.exe"));
                candidates.Add(Path.Combine(hostRoot, "bin", configuration, "net8.0", "FLASH_ROUTER.dll"));
                candidates.Add(Path.Combine(hostRoot, "bin", configuration, "net8.0", "FLASH_ROUTER.exe"));
                candidates.Add(Path.Combine(hostRoot, "bin", configuration, "net8.0", "FLASH_ROUTER.dll"));
            }
        }

        var selected = candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
        if (selected is null)
        {
            throw new LauncherConfigurationException(
                "HOST_NOT_FOUND",
                "FLASH_ROUTER.exe or FLASH_ROUTER.dll was not found beside the launcher or in the workspace build output.");
        }

        return (selected, Path.GetExtension(selected).Equals(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveBrowser(LauncherSettings settings)
    {
        var configured = Environment.GetEnvironmentVariable("FLASHROUTER_BROWSER_PATH")
            ?? settings.BrowserPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }
}

public sealed class LauncherConfigurationException : Exception
{
    public LauncherConfigurationException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
