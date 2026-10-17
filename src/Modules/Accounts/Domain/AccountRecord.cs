namespace FlashAI.Modules.Accounts.Domain;

public sealed class AccountRecord
{
    private AccountRecord()
    {
    }

    public AccountRecord(
        Guid id,
        int numericId,
        string email,
        string normalizedEmail,
        string displayName,
        string normalizedDisplayName,
        string? notes,
        string? normalizedNotes,
        string provider,
        string normalizedProvider,
        bool isFavorite,
        string connectionStatus,
        string recordedPlan,
        string normalizedRecordedPlan,
        DateTimeOffset trackingDateUtc,
        string dataSource,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? lastCheckedAtUtc,
        string recordedBy,
        long version = 1)
    {
        Id = id;
        NumericId = numericId;
        Email = email;
        NormalizedEmail = normalizedEmail;
        DisplayName = displayName;
        NormalizedDisplayName = normalizedDisplayName;
        Notes = notes;
        NormalizedNotes = normalizedNotes;
        Provider = provider;
        NormalizedProvider = normalizedProvider;
        IsFavorite = isFavorite;
        ConnectionStatus = connectionStatus;
        RecordedPlan = recordedPlan;
        NormalizedRecordedPlan = normalizedRecordedPlan;
        TrackingDateUtc = trackingDateUtc;
        TrackingDateTicksUtc = trackingDateUtc.UtcTicks;
        DataSource = dataSource;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        LastCheckedAtUtc = lastCheckedAtUtc;
        RecordedBy = recordedBy;
        Version = version;
        LifecycleStatus = AccountStatus.Active;
    }

    public Guid Id { get; private set; }
    public int NumericId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string NormalizedDisplayName { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? NormalizedNotes { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string NormalizedProvider { get; private set; } = string.Empty;
    public bool IsFavorite { get; private set; }
    public string ConnectionStatus { get; private set; } = string.Empty;
    public string RecordedPlan { get; private set; } = string.Empty;
    public string NormalizedRecordedPlan { get; private set; } = string.Empty;
    public DateTimeOffset TrackingDateUtc { get; private set; }

    public long TrackingDateTicksUtc { get; private set; }
    public string DataSource { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastCheckedAtUtc { get; private set; }
    public string RecordedBy { get; private set; } = string.Empty;
    public AccountStatus LifecycleStatus { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public long Version { get; private set; }
    public List<AccountTagRecord> Tags { get; private set; } = [];

    public void UpdateProfile(
        string email,
        string normalizedEmail,
        string displayName,
        string normalizedDisplayName,
        string? notes,
        string? normalizedNotes,
        string provider,
        string normalizedProvider,
        string connectionStatus,
        string recordedPlan,
        string normalizedRecordedPlan,
        DateTimeOffset updatedAtUtc)
    {
        Email = email;
        NormalizedEmail = normalizedEmail;
        DisplayName = displayName;
        NormalizedDisplayName = normalizedDisplayName;
        Notes = notes;
        NormalizedNotes = normalizedNotes;
        Provider = provider;
        NormalizedProvider = normalizedProvider;
        ConnectionStatus = connectionStatus;
        RecordedPlan = recordedPlan;
        NormalizedRecordedPlan = normalizedRecordedPlan;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void SetFavorite(bool isFavorite, DateTimeOffset updatedAtUtc)
    {
        IsFavorite = isFavorite;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void Archive(DateTimeOffset archivedAtUtc)
    {
        IsArchived = true;
        ArchivedAtUtc = archivedAtUtc;
        LifecycleStatus = AccountStatus.Archived;
        UpdatedAtUtc = archivedAtUtc;
        Version++;
    }

    public void Restore(DateTimeOffset restoredAtUtc)
    {
        IsArchived = false;
        ArchivedAtUtc = null;
        LifecycleStatus = AccountStatus.Active;
        UpdatedAtUtc = restoredAtUtc;
        Version++;
    }
}

public sealed class AccountTagRecord
{
    private AccountTagRecord()
    {
    }

    public AccountTagRecord(Guid accountId, string value, string normalizedValue)
    {
        AccountId = accountId;
        Value = value;
        NormalizedValue = normalizedValue;
    }

    public Guid AccountId { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public string NormalizedValue { get; private set; } = string.Empty;
}
