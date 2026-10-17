using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using FlashAI.Foundation.Hosting;

namespace FlashAI.Cli;

public static class LauncherApp
{
    private const int Success = 0;
    private const int UsageError = 2;
    private const int RuntimeError = 3;
    private const int BrowserError = 4;

    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = LauncherCommandParser.Parse(args);
        if (!parsed.IsValid)
        {
            Console.Error.WriteLine($"Error: {parsed.Error}");
            PrintHelp();
            return UsageError;
        }

        if (parsed.Command == LauncherCommand.Help)
        {
            PrintHelp();
            return Success;
        }

        LauncherRuntimeOptions options;
        try
        {
            options = LauncherConfigurationResolver.Resolve();
        }
        catch (LauncherConfigurationException exception)
        {
            Console.Error.WriteLine($"{exception.ErrorCode}: {exception.Message}");
            return RuntimeError;
        }

        var log = new LauncherLog(options.LogFile);
        var client = new LauncherControlClient();
        try
        {
            return parsed.Command switch
            {
                LauncherCommand.StartAndOpen => await StartAndOpenAsync(options, client, log),
                LauncherCommand.Open => await OpenAsync(options, client, log),
                LauncherCommand.Status => await StatusAsync(options, client),
                LauncherCommand.Stop => await StopAsync(options, client, log),
                LauncherCommand.Restart => await RestartAsync(options, client, log),
                LauncherCommand.Logs => ShowLogs(options),
                LauncherCommand.Version => ShowVersion(options),
                _ => UsageError
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Write("launcher.error", exception.GetType().Name);
            Console.Error.WriteLine($"Runtime error: {exception.Message}");
            return RuntimeError;
        }
    }

    private static async Task<int> StartAndOpenAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client,
        LauncherLog log)
    {
        var status = await EnsureRunningAsync(options, client, log);
        if (!status.Ready)
        {
            return RuntimeError;
        }

        var browser = BrowserAutoOpener.Open(options);
        Console.WriteLine(browser.Message);
        return browser.Success ? Success : BrowserError;
    }

    private static async Task<int> OpenAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client,
        LauncherLog log)
    {
        var status = await EnsureRunningAsync(options, client, log);
        if (!status.Ready)
        {
            return RuntimeError;
        }

        var browser = BrowserAutoOpener.Open(options);
        Console.WriteLine(browser.Message);
        return browser.Success ? Success : BrowserError;
    }

    private static async Task<int> StatusAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client)
    {
        var response = await client.SendAsync(options, "ping", TimeSpan.FromMilliseconds(700));
        if (response is null || !response.Accepted)
        {
            Console.WriteLine("Status: Stopped");
            Console.WriteLine($"Endpoint: {options.Endpoint}");
            Console.WriteLine("Health: no authenticated FLASH ROUTER control channel");
            return Success;
        }

        var ready = await IsReadyAsync(options.Endpoint, TimeSpan.FromSeconds(2));
        Console.WriteLine("Status: Running");
        Console.WriteLine($"PID: {response.ProcessId}");
        Console.WriteLine($"Version: {response.Version}");
        Console.WriteLine($"Endpoint: {response.Endpoint}");
        Console.WriteLine($"Health: {(ready ? "Ready" : "Starting")}");
        return Success;
    }

    private static async Task<int> StopAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client,
        LauncherLog log)
    {
        var response = await client.SendAsync(options, "stop", TimeSpan.FromSeconds(2));
        if (response is null || !response.Accepted)
        {
            Console.WriteLine("Status: Stopped");
            return Success;
        }

        log.Write("launcher.stop", "managed host stop requested");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(150);
            if (await client.SendAsync(options, "ping", TimeSpan.FromMilliseconds(250)) is null)
            {
                Console.WriteLine("Status: Stopped");
                return Success;
            }
        }

        Console.Error.WriteLine("STOP_TIMEOUT: the managed host did not exit gracefully within 10 seconds.");
        return RuntimeError;
    }

    private static async Task<int> RestartAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client,
        LauncherLog log)
    {
        var stopResult = await StopAsync(options, client, log);
        if (stopResult != Success)
        {
            return stopResult;
        }

        return await StartAndOpenAsync(options, client, log);
    }

    private static int ShowLogs(LauncherRuntimeOptions options)
    {
        if (!File.Exists(options.LogFile))
        {
            Console.WriteLine("No launcher log has been written.");
            return Success;
        }

        foreach (var line in File.ReadLines(options.LogFile).TakeLast(100))
        {
            Console.WriteLine(line);
        }

        return Success;
    }

    private static int ShowVersion(LauncherRuntimeOptions options)
    {
        var cliVersion = typeof(LauncherApp).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        Console.WriteLine($"flashrouter CLI {cliVersion}");
        Console.WriteLine($"Host: {Path.GetFileName(options.HostPath)}");
        return Success;
    }

    private static async Task<(bool Ready, ManagedHostControlResponse? Response)> EnsureRunningAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client,
        LauncherLog log)
    {
        var existing = await WaitForReadyAsync(options, client, TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
        if (existing.Response is not null)
        {
            return (true, existing.Response);
        }

        if (existing.ControlStillResponsive)
        {
            Console.Error.WriteLine("START_TIMEOUT: the existing FLASH ROUTER host did not report readiness.");
            return (false, null);
        }

        using var gate = LauncherMutex.Acquire(options.DataRoot);
        existing = await WaitForReadyAsync(options, client, TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
        if (existing.Response is not null)
        {
            return (true, existing.Response);
        }

        if (existing.ControlStillResponsive)
        {
            Console.Error.WriteLine("START_TIMEOUT: the existing FLASH ROUTER host did not report readiness.");
            return (false, null);
        }

        var process = StartHost(options, log);
        log.Write("launcher.start", $"host start requested for {Path.GetFileName(options.HostPath)}");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.StartupTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                Console.Error.WriteLine("HOST_EXITED: FLASH ROUTER exited before readiness.");
                return (false, null);
            }

            var response = await client.SendAsync(options, "ping", TimeSpan.FromMilliseconds(400));
            if (response is not null && response.Accepted && await IsReadyAsync(options.Endpoint, TimeSpan.FromSeconds(1)))
            {
                return (true, response);
            }

            await Task.Delay(200);
        }

        Console.Error.WriteLine("START_TIMEOUT: FLASH ROUTER did not report readiness. No unrelated process was stopped.");
        return (false, null);
    }

    private static async Task<(ManagedHostControlResponse? Response, bool ControlStillResponsive)> WaitForReadyAsync(
        LauncherRuntimeOptions options,
        LauncherControlClient client,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var controlStillResponsive = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.SendAsync(options, "ping", TimeSpan.FromMilliseconds(400));
            if (response is null || !response.Accepted)
            {
                return (null, controlStillResponsive);
            }

            controlStillResponsive = true;
            if (await IsReadyAsync(options.Endpoint, TimeSpan.FromSeconds(1)))
            {
                return (response, true);
            }

            await Task.Delay(200);
        }

        return (null, controlStillResponsive);
    }

    private static Process StartHost(LauncherRuntimeOptions options, LauncherLog log)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.HostIsExecutable ? options.HostPath : "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(options.HostPath) ?? options.InstallationRoot
        };
        if (options.HostIsExecutable)
        {
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(options.Endpoint.AbsoluteUri);
        }
        else
        {
            startInfo.ArgumentList.Add(options.HostPath);
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(options.Endpoint.AbsoluteUri);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = options.Endpoint.AbsoluteUri;
        startInfo.Environment["FlashAI__WorkspaceRoot"] = options.DataRoot;
        startInfo.Environment["FlashAI__AccountStorageMode"] = "Integrated";
        startInfo.Environment["FLASHROUTER_MANAGED_HOST"] = "1";

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("The FLASH ROUTER process could not be started.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            log.Write("launcher.start_failed", exception.GetType().Name);
            throw new InvalidOperationException("The FLASH ROUTER host executable could not be started.", exception);
        }
    }

    private static async Task<bool> IsReadyAsync(Uri endpoint, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(new Uri(endpoint, ManagedHostControl.ReadyPath));
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("FLASH ROUTER command launcher");
        Console.WriteLine();
        Console.WriteLine("Usage: flashrouter [command]");
        Console.WriteLine();
        Console.WriteLine("  flashrouter          Start the managed host if needed, then open the workspace.");
        Console.WriteLine("  flashrouter open     Open the workspace in the running or newly started host.");
        Console.WriteLine("  flashrouter status   Show managed PID, version, endpoint, and readiness.");
        Console.WriteLine("  flashrouter stop     Gracefully stop only the authenticated managed host.");
        Console.WriteLine("  flashrouter restart  Gracefully stop and start the managed host.");
        Console.WriteLine("  flashrouter logs     Show the redacted launcher log tail.");
        Console.WriteLine("  flashrouter version  Show launcher and host information.");
        Console.WriteLine("  flashrouter help     Show this help.");
    }
}

internal sealed class LauncherLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public LauncherLog(string path)
    {
        _path = path;
    }

    public void Write(string eventName, string detail)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = $"{DateTimeOffset.UtcNow:O}\t{eventName}\t{detail}";
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Launcher logging must not prevent the requested lifecycle operation.
        }
        catch (UnauthorizedAccessException)
        {
            // Launcher logging must not prevent the requested lifecycle operation.
        }
    }
}

internal static class BrowserAutoOpener
{
    public static (bool Success, string Message) Open(LauncherRuntimeOptions options)
    {
        var url = options.Endpoint.AbsoluteUri;
        if (string.Equals(Environment.GetEnvironmentVariable("FLASHROUTER_NO_BROWSER"), "1", StringComparison.Ordinal))
        {
            return (true, $"Host ready at {url} (browser auto-open disabled for this run).");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(options.BrowserPath) && File.Exists(options.BrowserPath))
            {
                var chrome = new ProcessStartInfo
                {
                    FileName = options.BrowserPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                chrome.ArgumentList.Add("--new-window");
                chrome.ArgumentList.Add(url);
                Process.Start(chrome);
                return (true, $"Host ready; opened Chrome at {url}.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return (true, $"Host ready; opened the default browser at {url}.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (false, $"Host ready at {url}, but browser auto-open failed: {exception.Message}");
        }
    }
}

internal sealed class LauncherMutex : IDisposable
{
    private readonly Semaphore _semaphore;

    private LauncherMutex(Semaphore semaphore)
    {
        _semaphore = semaphore;
    }

    public static LauncherMutex Acquire(string dataRoot)
    {
        var semaphore = new Semaphore(1, 1, $"Local\\FlashRouterLauncher-{ManagedHostControl.GetPipeName(dataRoot)}");
        try
        {
            if (!semaphore.WaitOne(TimeSpan.FromSeconds(10)))
            {
                semaphore.Dispose();
                throw new InvalidOperationException("Another FLASH ROUTER launcher operation is in progress.");
            }

            return new LauncherMutex(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // The gate may already have been released during a failed operation.
        }
        finally
        {
            _semaphore.Dispose();
        }
    }
}
