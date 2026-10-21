namespace FlashAI.Modules.Connections.Contracts;

public enum LiveProviderCertificationMode
{
    ExistingAuthenticatedSessionOnly,
    ExistingAuthenticatedSessionUsageApi
}

public enum LiveProviderCertificationStatus
{
    Blocked,
    Completed,
    Inconclusive,
    Failed,
    Cancelled
}

public sealed record LiveProviderAuthorization(
    Guid AuthorizationId,
    Guid AccountId,
    string ProviderId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool InspectExistingAuthenticatedSession,
    bool NavigateVisibleAccountUi,
    bool ReadProviderFields,
    bool PersistNonSecretObservations,
    bool InitiateNewAuthentication = false,
    bool SubmitStoredCredentials = false,
    bool SubmitTotp = false,
    bool CaptureScreenshots = false,
    bool InspectUsageOrBilling = false,
    bool UseProviderApi = false,
    string ScopeReference = "explicit-user-scope",
    Guid? AuthorizedAttemptId = null,
    bool AllowPendingSessionObservation = false)
{
    public bool IsActive(DateTimeOffset nowUtc)
        => AuthorizationId != Guid.Empty
            && AccountId != Guid.Empty
            && !string.IsNullOrWhiteSpace(ProviderId)
            && IssuedAtUtc <= nowUtc
            && nowUtc < ExpiresAtUtc;

    public bool IsReadOnlyExistingSessionScope
        => InspectExistingAuthenticatedSession
            && NavigateVisibleAccountUi
            && ReadProviderFields
            && !InitiateNewAuthentication
            && !SubmitStoredCredentials
            && !SubmitTotp
            && !CaptureScreenshots
            && !InspectUsageOrBilling
            && !UseProviderApi;

    public bool IsReadOnlyChatGptUsageApiScope
        => InspectExistingAuthenticatedSession
            && NavigateVisibleAccountUi
            && ReadProviderFields
            && InspectUsageOrBilling
            && UseProviderApi
            && !InitiateNewAuthentication
            && !SubmitStoredCredentials
            && !SubmitTotp
            && !CaptureScreenshots;

    public static LiveProviderAuthorization ReadOnlyExistingSession(
        Guid accountId,
        string providerId,
        bool persistNonSecretObservations,
        DateTimeOffset issuedAtUtc,
        TimeSpan lifetime,
        string scopeReference = "explicit-user-scope",
        Guid? authorizedAttemptId = null,
        bool allowPendingSessionObservation = false)
        => new(
            Guid.NewGuid(),
            accountId,
            providerId,
            issuedAtUtc,
            issuedAtUtc.Add(lifetime),
            InspectExistingAuthenticatedSession: true,
            NavigateVisibleAccountUi: true,
            ReadProviderFields: true,
            PersistNonSecretObservations: persistNonSecretObservations,
            ScopeReference: scopeReference,
            AuthorizedAttemptId: authorizedAttemptId,
            AllowPendingSessionObservation: allowPendingSessionObservation);

    public static LiveProviderAuthorization ReadOnlyExistingSessionUsageApi(
        Guid accountId,
        string providerId,
        bool persistNonSecretObservations,
        DateTimeOffset issuedAtUtc,
        TimeSpan lifetime,
        string scopeReference = "explicit-user-scope",
        Guid? authorizedAttemptId = null,
        bool allowPendingSessionObservation = false)
        => new(
            Guid.NewGuid(),
            accountId,
            providerId,
            issuedAtUtc,
            issuedAtUtc.Add(lifetime),
            InspectExistingAuthenticatedSession: true,
            NavigateVisibleAccountUi: true,
            ReadProviderFields: true,
            PersistNonSecretObservations: persistNonSecretObservations,
            InspectUsageOrBilling: true,
            UseProviderApi: true,
            ScopeReference: scopeReference,
            AuthorizedAttemptId: authorizedAttemptId,
            AllowPendingSessionObservation: allowPendingSessionObservation);
}

public sealed record LiveProviderCertificationRequest(
    Guid AccountId,
    string ProviderId,
    LiveProviderAuthorization? Authorization,
    LiveProviderCertificationMode Mode = LiveProviderCertificationMode.ExistingAuthenticatedSessionOnly,
    TimeSpan? ObservationTimeout = null);

public sealed record LiveProviderCertificationResult(
    LiveProviderCertificationStatus Status,
    string Code,
    string Message,
    Guid CorrelationId,
    Guid AccountId,
    string ProviderId,
    BrowserRuntimeSnapshot Snapshot,
    ProviderAccountObservationSnapshot? Observation = null,
    IdentityVerificationStatus IdentityStatus = IdentityVerificationStatus.NotChecked,
    bool LocalObservationPersisted = false,
    DateTimeOffset CompletedAtUtc = default)
{
    public bool IsSuccess => Status == LiveProviderCertificationStatus.Completed;
}
