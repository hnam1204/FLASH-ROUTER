namespace FlashAI.Foundation.Persistence;

public sealed record LocalDataPaths(string Root, string? AutomationRootOverride = null)
{
    public string Data => Path.Combine(Root, ".local", "data");
    public string Security => Path.Combine(Root, ".local", "security");
    public string Backups => Path.Combine(Root, ".local", "backups");
    public string Logs => Path.Combine(Root, ".local", "logs");
    public string Temp => Path.Combine(Root, ".local", "temp");
    public string TestData => Path.Combine(Root, ".local", "test-data");
    public string Runtime => Path.Combine(Root, ".local", "runtime");

    // Automation profiles/logs are isolated from the legacy .local data roots.
    public string AutomationRoot => Path.GetFullPath(
        AutomationRootOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlashRouter"));
    public string AutomationBrowserProfiles => Path.Combine(AutomationRoot, "BrowserProfiles");
    public string AutomationLogs => Path.Combine(AutomationRoot, "Logs");

    public string DatabaseFile => Path.Combine(Data, "flashrouter.db");
    public string ControlSecretFile => Path.Combine(Runtime, "control.secret");
    public string InstanceMetadataFile => Path.Combine(Runtime, "instance.json");
    public string LauncherLogFile => Path.Combine(Logs, "launcher.log");
}
