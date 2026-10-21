namespace FlashAI.Modules.BackupRestore.Integration;

public static class PortableBackupFormat
{
    public const string CurrentVersion = "be-06-local-1";

    public const string PlaintextSecretsAllowed = "false";

    public const string DatabasePayloadFileName = "database.flashdb";

    public const string ManifestFileName = "manifest.json";

    public const string VaultDirectoryName = "vault";
}

/// <summary>Serializes backup creation, validation, and account-scoped backup scrubbing.</summary>
public static class BackupMaintenanceGate
{
    public static SemaphoreSlim Semaphore { get; } = new(1, 1);
}
