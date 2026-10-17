namespace FlashAI.Modules.Accounts.Contracts;

public sealed record AccountSummary(Guid AccountId, string DisplayName, bool IsArchived);
