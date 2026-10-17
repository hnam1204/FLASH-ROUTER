namespace FlashAI.Modules.Connections.Domain;

public sealed class BrowserProfileBindingRecord
{
    private BrowserProfileBindingRecord()
    {
    }

    public BrowserProfileBindingRecord(
        Guid id,
        Guid accountId,
        string providerId,
        string browserKind,
        string opaqueProfileIdentifier,
        string profileDirectoryReference,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        AccountId = accountId;
        ProviderId = providerId;
        BrowserKind = browserKind;
        OpaqueProfileIdentifier = opaqueProfileIdentifier;
        ProfileDirectoryReference = profileDirectoryReference;
        CreatedAtUtc = createdAtUtc;
        State = BrowserProfileState.Ready.ToString();
        ConcurrencyVersion = 1;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public string BrowserKind { get; private set; } = string.Empty;
    public string OpaqueProfileIdentifier { get; private set; } = string.Empty;
    public string ProfileDirectoryReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastOpenedAtUtc { get; private set; }
    public string State { get; private set; } = string.Empty;
    public long ConcurrencyVersion { get; private set; }

    public void MarkOpened(DateTimeOffset openedAtUtc)
    {
        LastOpenedAtUtc = openedAtUtc;
        State = BrowserProfileState.Ready.ToString();
        ConcurrencyVersion++;
    }

    public void MarkLaunchFailed(DateTimeOffset failedAtUtc)
    {
        State = BrowserProfileState.LaunchFailed.ToString();
        ConcurrencyVersion++;
    }
}

public enum BrowserProfileState
{
    Ready,
    LaunchFailed,
    Archived
}
