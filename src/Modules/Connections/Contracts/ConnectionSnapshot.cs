namespace FlashAI.Modules.Connections.Contracts;

public sealed record ConnectionSnapshot(Guid AccountId, string Status, DateTimeOffset? VerifiedAt, string[] Scopes);
