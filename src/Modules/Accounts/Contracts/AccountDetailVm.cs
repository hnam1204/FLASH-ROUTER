namespace FlashAI.Modules.Accounts.Contracts;

public sealed record AccountDetailVm(
    Guid AccountId,
    int NumericId,
    string Email,
    string DisplayName,
    string? Notes,
    string Provider,
    bool IsFavorite,
    string Status,
    string StatusLabel,
    string RecordedPlan,
    DateTimeOffset TrackingDate,
    string DataSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastCheckedAt,
    string RecordedBy,
    IReadOnlyList<string> Tags,
    bool IsArchived = false,
    long Version = 1);
