using System.Text.Json;

namespace FlashAI.Cli;

public enum LauncherCommand
{
    StartAndOpen,
    Open,
    Status,
    Stop,
    Restart,
    Logs,
    Version,
    Help
}

public sealed record ParsedLauncherCommand(LauncherCommand Command, string? Error)
{
    public bool IsValid => Error is null;
}

public sealed record LauncherSettings(
    string? DataRoot,
    string? Endpoint,
    string? HostPath,
    string? BrowserPath,
    int? StartupTimeoutSeconds);

public sealed record LauncherRuntimeOptions(
    string InstallationRoot,
    string DataRoot,
    Uri Endpoint,
    string HostPath,
    bool HostIsExecutable,
    string BrowserPath,
    int StartupTimeoutSeconds,
    string LogFile)
{
    public string PipeName => FlashAI.Foundation.Hosting.ManagedHostControl.GetPipeName(DataRoot);
    public string SecretFile => new FlashAI.Foundation.Persistence.LocalDataPaths(DataRoot).ControlSecretFile;
    public string MetadataFile => new FlashAI.Foundation.Persistence.LocalDataPaths(DataRoot).InstanceMetadataFile;
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
