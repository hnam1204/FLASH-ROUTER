namespace FlashAI.Modules.Connections.Domain;

public enum ConnectionKind
{
    WebOAuth,
    ApiKey,
    AdminApiKey,
    Manual
}

public sealed class ProviderConnectionRecord
{
    private ProviderConnectionRecord()
    {
    }

    public ProviderConnectionRecord(
        Guid id,
        Guid accountId,
        string providerId,
        ConnectionKind connectionKind,
        string displayLabel,
        string? externalIdentity,
        ConnectionStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        AccountId = accountId;
        ProviderId = providerId;
        ConnectionKind = connectionKind;
        DisplayLabel = displayLabel;
        ExternalIdentity = externalIdentity;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public ConnectionKind ConnectionKind { get; private set; }
    public string DisplayLabel { get; private set; } = string.Empty;
    public string? ExternalIdentity { get; private set; }
    public ConnectionStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastCheckedAtUtc { get; private set; }

    public void MarkStatus(ConnectionStatus status, DateTimeOffset updatedAtUtc)
    {
        Status = status;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void MarkChecked(DateTimeOffset checkedAtUtc)
    {
        LastCheckedAtUtc = checkedAtUtc;
        UpdatedAtUtc = checkedAtUtc;
    }
}
