namespace FlashAI.Modules.Accounts.Contracts;

public sealed record AccountListItemVm(
    Guid AccountId,
    int NumericId,
    string Email,
    string DisplayName,
    string Provider,
    bool IsFavorite,
    string Status,
    string StatusLabel,
    string RecordedPlan,
    DateTimeOffset TrackingDate,
    IReadOnlyList<string> Tags,
    bool IsArchived = false);
