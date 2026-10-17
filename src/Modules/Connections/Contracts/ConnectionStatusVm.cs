namespace FlashAI.Modules.Connections.Contracts;

public sealed record ConnectionStatusVm(
    Guid AccountId,
    string Provider,
    string State,
    string StateLabel,
    string? AuthorizedIdentity,
    DateTimeOffset? AuthorizedAt,
    DateTimeOffset? LastVerifiedAt,
    string? SafeErrorSummary,
    string ActionPrompt,
    bool IsBackendIntegrated = false,
    IReadOnlyList<string>? GrantedScopes = null,
    DateTimeOffset? AccessTokenExpiresAt = null,
    bool ChatGptPlanUsageEnabled = false);
