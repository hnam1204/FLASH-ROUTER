namespace FlashAI.Modules.BackupRestore.Integration;

public static class PortableBackupFormat
{
    public const string CurrentVersion = "be-06-local-1";

    public const string PlaintextSecretsAllowed = "false";

    public const string DatabasePayloadFileName = "database.flashdb";

    public const string ManifestFileName = "manifest.json";

    public const string VaultDirectoryName = "vault";
}
