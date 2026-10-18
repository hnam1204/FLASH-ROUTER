namespace FlashAI.Foundation.Contracts;

/// <summary>
/// Durable, account-owned projection of browser authentication evidence.
/// The strings are provider-neutral so Connections does not depend on the
/// Accounts module's domain types.
/// </summary>
public sealed record AccountAuthenticationStateUpdate(
    Guid AccountId,
    Guid AttemptId,
    string ProviderId,
    string AuthenticationState,
    string IdentityVerificationState,
    DateTimeOffset AttemptedAtUtc,
    DateTimeOffset? BrowserSessionObservedAtUtc = null,
    DateTimeOffset? AuthenticatedSessionConfirmedAtUtc = null,
    DateTimeOffset? IdentityVerifiedAtUtc = null,
    DateTimeOffset? ObservedAtUtc = null,
    string? EvidenceSource = null,
    string? SanitizedFailureCode = null,
    string? ObservedProviderEmail = null,
    string? IdentityEvidenceSource = null,
    string? IdentityReasonCode = null);

public sealed record AccountAuthenticationStateSnapshot(
    Guid AccountId,
    Guid? AttemptId,
    string ProviderId,
    string AuthenticationState,
    string IdentityVerificationState,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastBrowserSessionObservedAtUtc,
    DateTimeOffset? LastAuthenticatedSessionConfirmedAtUtc,
    DateTimeOffset? LastIdentityVerifiedAtUtc,
    DateTimeOffset? LastObservationAtUtc,
    string? EvidenceSource,
    string? SanitizedFailureCode,
    string? ObservedProviderEmail,
    string? IdentityEvidenceSource,
    string? IdentityReasonCode);

public interface IAccountAuthenticationStateStore
{
    Task RecordAsync(
        AccountAuthenticationStateUpdate update,
        CancellationToken cancellationToken = default);
}

public interface IAccountAuthenticationStateReader
{
    Task<AccountAuthenticationStateSnapshot?> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
