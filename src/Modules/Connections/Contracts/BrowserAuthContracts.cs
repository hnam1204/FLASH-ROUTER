namespace FlashAI.Modules.Connections.Contracts;

public enum BrowserAuthState
{
    NotStarted,
    BrowserStarting,
    Navigating,
    ObservingPostLogin,
    PostLoginPending,
    GuestLandingLoginAvailable,
    EmailRequired,
    PasswordRequired,
    MfaRequired,
    ExternalProviderRequired,
    HumanActionRequired,
    WaitingForUser,
    BrowserSessionObserved,
    // Kept as an alias so existing callers remain source-compatible while the
    // durable projection can distinguish confirmed authentication explicitly.
    AuthenticatedSessionConfirmed = BrowserSessionObserved,
    ProfileInUse,
    Failed,
    Cancelled,
    TimedOut,
    Unknown,
    IdentityMismatch,
    IdentityUnverified
}

public enum AuthenticationObservationStatus
{
    NotChecked,
    Checking,
    BrowserSessionObserved,
    AuthenticatedSessionConfirmed,
    PostLoginPending,
    LoginRequired,
    SessionExpired,
    Failed,
    Unknown
}

public enum IdentityVerificationStatus
{
    NotChecked,
    Checking,
    IdentityObserved,
    IdentityVerified,
    Mismatch,
    Unverified,
    PartiallyResolved,
    Unsupported,
    Unauthorized,
    Failed,
    Unknown
}

public enum ProviderMetadataStatus
{
    NotChecked,
    Pending,
    Available,
    PartiallyAvailable,
    Unsupported,
    Unauthorized,
    Unavailable,
    Failed,
    Stale
}

public enum UsageObservationStatus
{
    NotScheduled,
    Scheduled,
    Checking,
    Available,
    Unsupported,
    Unauthorized,
    RateLimited,
    Unavailable,
    Failed,
    Stale
}

public enum ProviderAccountObservationField
{
    DisplayName,
    Email,
    Plan,
    MfaConfiguration,
    UsageQuota
}

public enum ProviderAccountObservationFieldStatus
{
    Observed,
    Unavailable,
    Unsupported,
    Failed
}

public sealed record ProviderAccountFieldEvidence(
    ProviderAccountObservationField Field,
    string? Value,
    string Source,
    string EvidenceType,
    string Confidence,
    ProviderAccountObservationFieldStatus Status,
    DateTimeOffset ObservedAtUtc,
    string? FailureReasonCode = null);

public sealed record ProviderAccountObservation(
    Guid AccountId,
    Guid AttemptId,
    Guid BrowserProfileBindingId,
    string ProviderId,
    string BrowserContextId,
    DateTimeOffset ObservedAtUtc,
    string Locale,
    bool AuthenticatedShellDetected,
    bool AccountMenuDetected,
    string ObservationStatus,
    string EvidenceSurface,
    string? FailureReasonCode,
    ProviderAccountFieldEvidence DisplayName,
    ProviderAccountFieldEvidence Email,
    ProviderAccountFieldEvidence Plan,
    ProviderAccountFieldEvidence MfaConfiguration,
    ProviderAccountFieldEvidence UsageQuota);

public sealed record ProviderAccountObservationSnapshot(
    Guid ObservationId,
    Guid AccountId,
    Guid AttemptId,
    Guid BrowserProfileBindingId,
    string ProviderId,
    string BrowserContextId,
    DateTimeOffset ObservedAtUtc,
    string Locale,
    bool AuthenticatedShellDetected,
    bool AccountMenuDetected,
    string ObservationStatus,
    string EvidenceSurface,
    string? FailureReasonCode,
    IReadOnlyList<ProviderAccountFieldEvidence> Fields);

public sealed record ChatGptUsageWindowSnapshot(
    decimal? UsedPercent,
    long? LimitWindowSeconds,
    DateTimeOffset? ResetAtUtc,
    long? ResetAfterSeconds);

public sealed record ChatGptWebUsageSnapshot(
    string? PlanType,
    ChatGptUsageWindowSnapshot? PrimaryWindow,
    ChatGptUsageWindowSnapshot? SecondaryWindow,
    DateTimeOffset ObservedAtUtc);

public sealed record ChatGptUsageApiReadResult(
    bool IsSuccess,
    string Status,
    string Message,
    ChatGptWebUsageSnapshot? Snapshot = null,
    string? VerifiedEmail = null);

public sealed record AccountObservationResult(
    bool IsSuccess,
    string Status,
    string Message,
    BrowserRuntimeSnapshot Snapshot,
    ProviderAccountObservationSnapshot? Observation = null,
    IdentityVerificationStatus IdentityStatus = IdentityVerificationStatus.NotChecked,
    bool LocalObservationPersisted = false);

public enum BrowserAuthCredentialReadinessKind
{
    CredentialReferenceMissing,
    CredentialReferencePresent,
    VaultLocked,
    VaultUnlocked,
    CredentialPayloadUnavailable,
    CredentialOwnershipMismatch,
    CredentialProviderMismatch,
    CredentialRotationRequired,
    CredentialReady,
    CredentialReadFailure,
    ProtectionContextMismatch,
    ProtectionKeyUnavailable,
    LegacyUnlockRequired,
    CredentialCorrupted,
    UnauthorizedAccess,
    VaultStorageUnavailable
}

public enum BrowserAuthAction
{
    ActivateLoginEntry,
    FillEmail,
    FillPassword,
    FillTotp
}

public enum BrowserAuthField
{
    Email,
    Password,
    Totp
}

public enum BrowserPageState
{
    Unknown,
    GuestLandingLoginAvailable,
    EmailRequired,
    PasswordRequired,
    MfaRequired,
    ExternalProviderRequired,
    Captcha,
    Passkey,
    RecoveryCode,
    SmsOrEmailCode,
    RateLimited,
    Authenticated,
    InvalidCredentials
}

public enum MfaObservationKind
{
    Unknown,
    MfaFormUnchanged,
    MfaSubmissionProcessing,
    MfaValidationError,
    AdditionalMfaChallenge,
    HumanConfirmationRequired,
    MfaResolved,
    AuthenticatedShellObserved
}

public sealed record BrowserPageSnapshot(
    Uri CurrentUri,
    BrowserPageState State,
    bool HasEmailField = false,
    bool HasPasswordField = false,
    bool HasTotpField = false,
    bool HasGuestLoginEntry = false,
    int GuestLoginEntryCount = 0,
    bool IsPageReady = true,
    int EmailContinueActionCount = 0,
    bool IsEmailModal = false,
    string? ObservationFailureCode = null,
    string? ObservationFailureMessage = null,
    MfaObservationKind MfaObservation = MfaObservationKind.Unknown)
{
    public bool HasEmailContinueAction => EmailContinueActionCount == 1;
}

public sealed record BrowserAuthAttempt(
    Guid AttemptId,
    Guid AccountId,
    string ProviderId,
    Guid BrowserProfileBindingId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    BrowserAuthState State,
    Guid? ConnectionId = null,
    Guid? VaultCredentialReferenceId = null);

public sealed class AutoLoginAttemptConsent
{
    public AutoLoginAttemptConsent(
        Guid attemptId,
        Guid accountId,
        string providerId,
        Guid browserProfileBindingId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        AttemptId = attemptId;
        AccountId = accountId;
        ProviderId = providerId;
        BrowserProfileBindingId = browserProfileBindingId;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid AttemptId { get; }
    public Guid AccountId { get; }
    public string ProviderId { get; }
    public Guid BrowserProfileBindingId { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public bool AllowsEmail { get; init; } = true;
    public bool AllowsPassword { get; init; } = true;
    public bool AllowsTotp { get; init; } = true;
    public bool CredentialRotationAttested { get; init; }
    public bool IsRevoked { get; private set; }
    public bool IsConsumed { get; private set; }

    public bool IsActive(DateTimeOffset nowUtc)
        => !IsRevoked && !IsConsumed && nowUtc < ExpiresAtUtc;

    public bool Allows(BrowserAuthAction action)
        => action switch
        {
            BrowserAuthAction.FillEmail => AllowsEmail,
            BrowserAuthAction.FillPassword => AllowsPassword,
            BrowserAuthAction.FillTotp => AllowsTotp,
            _ => false
        };

    public void Revoke() => IsRevoked = true;

    public void Consume() => IsConsumed = true;
}

public sealed record BrowserAuthPreflight(
    Guid AccountId,
    string ProviderId,
    string OfficialDestination,
    string? MaskedAccountEmail,
    bool VaultUnlocked,
    bool CredentialReferenceReady,
    string BrowserProfileStatus,
    bool IsReady,
    string Status,
    string Message,
    BrowserAuthTimingSnapshot? Timing = null,
    BrowserAuthCredentialReadinessKind CredentialReadiness = BrowserAuthCredentialReadinessKind.CredentialReferenceMissing,
    Guid? ConnectionId = null,
    Guid? VaultCredentialReferenceId = null,
    bool BrowserProfileRetryAvailable = false,
    string? BrowserProfileFailureStatus = null);

public sealed record BrowserAuthTimingSnapshot(
    long? PreflightDurationMs = null,
    long? ProfileLeaseDurationMs = null,
    long? ChromeLaunchDurationMs = null,
    long? OfficialNavigationDurationMs = null,
    long? ExistingSessionCheckDurationMs = null,
    long? LoginCtaDurationMs = null,
    long? EmailStepDurationMs = null,
    long? PasswordStepDurationMs = null,
    long? MfaStepDurationMs = null,
    long? AuthenticatedObservationDurationMs = null,
    long? TotalAttemptDurationMs = null);

public sealed record BrowserAuthProgress(
    string Stage,
    BrowserAuthState State,
    string Message,
    Guid? AttemptId = null,
    string? EventCode = null,
    long? DurationMs = null,
    bool IsTerminal = false);

public sealed record BrowserSessionObservation(
    Guid AttemptId,
    Guid AccountId,
    Guid BrowserProfileBindingId,
    string ProviderId,
    DateTimeOffset ObservedAtUtc,
    string ObservationMethod,
    string SafeNavigationStatus,
    AuthenticationObservationStatus AuthenticationStatus = AuthenticationObservationStatus.BrowserSessionObserved,
    IdentityVerificationStatus IdentityStatus = IdentityVerificationStatus.Unknown,
    string? SanitizedFailureCode = null);

public sealed record AuthenticationObservationResult(
    Guid AccountId,
    Guid AttemptId,
    string ProviderId,
    AuthenticationObservationStatus Status,
    IdentityVerificationStatus IdentityStatus,
    DateTimeOffset ObservedAtUtc,
    string EvidenceSource,
    string? SanitizedFailureCode,
    string? ObservedProviderEmail = null,
    string? IdentityEvidenceSource = null,
    string? IdentityReasonCode = null);

public sealed record BrowserIdentityEvidence(
    bool AuthenticatedShellDetected,
    bool AccountMenuDetected,
    string? ObservedEmail,
    string? ObservedDisplayName,
    string EvidenceSource,
    string? SafeReasonCode,
    DateTimeOffset ObservedAtUtc,
    ProviderAccountObservation? AccountObservation = null);

public sealed record ProviderIdentityVerificationRequest(
    Guid AccountId,
    Guid AttemptId,
    Guid BrowserProfileBindingId,
    string ProviderId,
    string IntendedAccountEmail,
    IBrowserAuthenticationSession Session);

public sealed record ProviderIdentityVerificationResult(
    Guid AccountId,
    Guid AttemptId,
    Guid BrowserProfileBindingId,
    string ProviderId,
    IdentityVerificationStatus Status,
    DateTimeOffset ObservedAtUtc,
    string EvidenceSource,
    string? ObservedProviderEmail,
    string? SafeReasonCode,
    ProviderAccountObservation? AccountObservation = null)
{
    public bool IsVerified => Status == IdentityVerificationStatus.IdentityVerified;
}

public sealed record BrowserAuthResult(
    bool IsSuccess,
    BrowserAuthState State,
    string Status,
    string Message,
    Guid? AttemptId = null,
    BrowserAuthAction? NextAction = null,
    int? TotpRemainingSeconds = null,
    BrowserSessionObservation? Observation = null,
    string? MaskedAccountEmail = null,
    BrowserAuthTimingSnapshot? Timing = null,
    AuthenticationObservationResult? ObservationResult = null,
    BrowserProfileSessionContext? CandidateProfile = null,
    string? CancellationReason = null,
    MfaObservationKind MfaObservation = MfaObservationKind.Unknown)
{
    public static BrowserAuthResult Failure(
        BrowserAuthState state,
        string status,
        string message,
        Guid? attemptId = null)
        => new(false, state, status, message, attemptId);
}

public sealed class BrowserAuthSessionException : Exception
{
    public BrowserAuthSessionException(
        string safeCode,
        string safeMessage,
        string? technicalExceptionType = null)
        : base(safeMessage)
    {
        SafeCode = safeCode;
        SafeMessage = safeMessage;
        TechnicalExceptionType = technicalExceptionType;
    }

    public string SafeCode { get; }
    public string SafeMessage { get; }
    public string? TechnicalExceptionType { get; }
}

public interface IBrowserAuthenticationSession : IAsyncDisposable
{
    Uri CurrentUri { get; }

    Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default);

    Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default);

    Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default);

    Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default);

    Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default);

    Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default);

    Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default);
}

public interface IBrowserAuthenticationSessionDiagnostics
{
    BrowserAuthTimingSnapshot Timing { get; }
}

/// <summary>
/// Optional capability for sessions that can inspect safe, visible account UI.
/// It deliberately does not expose cookies, headers, page HTML or private API data.
/// </summary>
public interface IBrowserIdentityObservationSession
{
    Task<BrowserIdentityEvidence> ReadIdentityEvidenceAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional capability for a managed Playwright context to inspect only the
/// visible, official account/settings surfaces. It never exposes browser state.
/// </summary>
public interface IBrowserAccountObservationSession
{
    Task<ProviderAccountObservation> ReadAccountObservationAsync(
        Guid accountId,
        Guid attemptId,
        Guid browserProfileBindingId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional, explicit read-only capability for the authenticated ChatGPT usage endpoint.
/// Implementations must use the current ChatGPT browser context and return only normalized fields.
/// </summary>
public interface IChatGptUsageApiObservationSession
{
    Task<ChatGptUsageApiReadResult> ReadUsageApiAsync(CancellationToken cancellationToken = default);
}

public interface IProviderAccountObservationStore
{
    Task SaveAsync(ProviderAccountObservation observation, CancellationToken cancellationToken = default);
}

public interface IProviderAccountObservationReader
{
    Task<ProviderAccountObservationSnapshot?> GetLatestAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default);
}

public interface IProviderAccountVerificationService
{
    Task<ProviderIdentityVerificationResult> VerifyAsync(
        ProviderIdentityVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IBrowserAuthenticationSessionFactory
{
    Task<IBrowserAuthenticationSession> OpenAsync(
        BrowserProfileSessionContext profile,
        Uri officialUri,
        CancellationToken cancellationToken = default);
}

public interface IBrowserPageStateDetector
{
    BrowserPageState Detect(BrowserPageSnapshot snapshot);
}

public interface IBrowserAuthOrchestrator
{
    Task<BrowserAuthPreflight> GetAutoLoginPreflightAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> AutoLoginAsync(
        Guid accountId,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> AutoLoginAsync(
        Guid accountId,
        bool credentialRotationAttested,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> ResumeAfterVaultUnlockAsync(
        Guid attemptId,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> StartAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> RefreshAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> ActivateLoginEntryAsync(
        Guid attemptId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> ConfirmAndFillAsync(
        Guid attemptId,
        BrowserAuthAction action,
        bool confirmed,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BrowserAuthResult> CancelAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BrowserAuthResult>> CancelAccountAttemptsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
