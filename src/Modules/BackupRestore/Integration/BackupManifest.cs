namespace FlashAI.Modules.BackupRestore.Integration;

public sealed record BackupManifest(
    string FormatVersion,
    Guid BackupId,
    string DisplayName,
    string BackupType,
    DateTimeOffset CreatedAtUtc,
    string DatabaseSchemaVersion,
    string SourceDatabaseId,
    long DatabasePayloadSizeBytes,
    string DatabasePayloadSha256,
    string IntegrityStatus,
    string EncryptionStatus,
    bool VaultCiphertextIncluded,
    int VaultFileCount,
    bool KeyRingIncluded,
    bool Portable,
    string RestoreCompatibility,
    int AccountCount,
    int ProviderConnectionCount,
    int ObservationCount,
    int CredentialReferenceCount);
