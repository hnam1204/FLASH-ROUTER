namespace FlashAI.Modules.Connections.Contracts;

public enum BrowserOpenTarget
{
    ChatGpt,
    OfficialUsage
}

public sealed record BrowserProfileBindingVm(
    Guid BindingId,
    Guid AccountId,
    string ProviderId,
    string BrowserKind,
    string OpaqueProfileIdentifier,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastOpenedAtUtc);

public sealed record BrowserLaunchResult(
    bool IsSuccess,
    string Status,
    string Message,
    BrowserProfileBindingVm? Binding = null);

public enum AccountBrowserRuntimeState
{
    Closed,
    Launching,
    Open,
    Closing,
    Crashed,
    Disconnected,
    ProfileInUse,
    Unknown
}

public sealed record BrowserRuntimeSnapshot(
    Guid AccountId,
    Guid? BrowserProfileBindingId,
    string ProviderId,
    AccountBrowserRuntimeState RuntimeState,
    AuthenticationObservationStatus AuthenticationState,
    IdentityVerificationStatus IdentityVerificationState,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? RuntimeStartedAtUtc = null,
    DateTimeOffset? LastClosedAtUtc = null,
    DateTimeOffset? LastSessionObservedAtUtc = null,
    DateTimeOffset? LastAuthenticatedSessionConfirmedAtUtc = null,
    string EvidenceSource = "Current process reconciliation",
    string? SanitizedFailureCode = null);

public sealed record BrowserOperationResult(
    bool IsSuccess,
    string Status,
    string Message,
    BrowserRuntimeSnapshot Snapshot);

public sealed record SessionObservationResult(
    bool IsSuccess,
    string Status,
    string Message,
    BrowserRuntimeSnapshot Snapshot,
    AuthenticationObservationResult? Observation = null);

public sealed record BrowserProfileSessionContext(
    Guid BindingId,
    Guid AccountId,
    string ProviderId,
    string OpaqueProfileIdentifier,
    string ProfileDirectory,
    string ChromeExecutablePath,
    BrowserProfileBindingVm Binding);

public sealed record BrowserProfilePreparationResult(
    bool IsSuccess,
    string Status,
    string Message,
    BrowserProfileSessionContext? Context = null);

public sealed record BrowserProfilePromotionResult(
    bool IsSuccess,
    string Status,
    string Message,
    BrowserProfileBindingVm? Binding = null)
{
    public static BrowserProfilePromotionResult Failure(string status, string message)
        => new(false, status, message);
}

public enum BrowserProfileLifecycleState
{
    Available,
    Opening,
    OwnedByManagedChrome,
    OwnedByPlaywright,
    InUseExternally,
    Closing,
    Closed,
    Failed
}

public enum BrowserProfileOwnerKind
{
    ManagedChrome,
    Playwright,
    External
}

public enum BrowserProfileLockState
{
    None,
    Active,
    StaleMetadata
}

public sealed record BrowserProfileOwnershipResult(
    bool IsAcquired,
    BrowserProfileLifecycleState State,
    BrowserProfileOwnerKind? OwnerKind,
    string Status,
    string Message,
    IDisposable? Lease = null);

public interface IChromeProfileLockInspector
{
    BrowserProfileLockState Inspect(string profileDirectory);
}

public interface IBrowserProfileOwnershipCoordinator
{
    BrowserProfileOwnershipResult TryAcquire(
        string profileDirectory,
        BrowserProfileOwnerKind ownerKind);
}

public sealed record ChromeLaunchResult(
    bool IsSuccess,
    string Status,
    string Message);

public interface IBrowserProfileUiPort
{
    Task<BrowserProfileBindingVm?> GetBindingAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default);

    Task<BrowserLaunchResult> OpenAsync(
        Guid accountId,
        BrowserOpenTarget target,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default);
}

public interface IAccountBrowserSessionManager
{
    Task<BrowserRuntimeSnapshot> GetStatusAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<BrowserOperationResult> OpenAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<BrowserOperationResult> CloseAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<BrowserOperationResult> ReopenAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<SessionObservationResult> CheckSessionAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task ReconcileAsync(CancellationToken cancellationToken = default);
}

public interface IAccountBrowserSessionRegistration
{
    Task RegisterManagedSessionAsync(
        Guid accountId,
        Guid attemptId,
        BrowserProfileSessionContext profile,
        IBrowserAuthenticationSession session,
        CancellationToken cancellationToken = default);

    Task UnregisterManagedSessionAsync(
        Guid accountId,
        Guid attemptId,
        CancellationToken cancellationToken = default);
}

public interface IChromeExecutableResolver
{
    string? Resolve();
}

public enum PlaywrightChromiumReadiness
{
    ChromiumMissing,
    ChromiumInstallRequired,
    ChromiumInstallFailed,
    ChromiumInstalled,
    ChromiumReady
}

public sealed record PlaywrightBrowserRuntimeStatus(
    PlaywrightChromiumReadiness Readiness,
    string PackageVersion,
    string? ExecutablePath,
    string BrowserCachePath,
    string? ExpectedRevision,
    string? ResolvedRevision,
    string Message)
{
    public bool IsReady => Readiness == PlaywrightChromiumReadiness.ChromiumReady;

    public string StatusCode => Readiness switch
    {
        PlaywrightChromiumReadiness.ChromiumReady => "CHROMIUM_READY",
        PlaywrightChromiumReadiness.ChromiumInstalled => "CHROMIUM_INSTALLED",
        PlaywrightChromiumReadiness.ChromiumInstallRequired => "CHROMIUM_INSTALL_REQUIRED",
        PlaywrightChromiumReadiness.ChromiumInstallFailed => "CHROMIUM_INSTALL_FAILED",
        _ => "CHROMIUM_MISSING"
    };
}

public interface IPlaywrightBrowserRuntime
{
    Task<PlaywrightBrowserRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<PlaywrightBrowserRuntimeStatus> InstallOrRepairAsync(CancellationToken cancellationToken = default);
}

public interface IChromeProcessLauncher
{
    bool TryLaunch(string executablePath, string profileDirectory, string officialUrl, out string? safeError);
}

public interface IChromeProcessController
{
    bool IsRunning(string profileDirectory);

    bool TryClose(string profileDirectory, out string? safeError);
}

public interface IChromeProcessLauncherDiagnostics
{
    ChromeLaunchResult TryLaunchDetailed(
        string executablePath,
        string profileDirectory,
        string officialUrl);
}

public interface IBrowserProfileManager
{
    Task<BrowserProfileBindingVm?> GetBindingAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default);

    Task<BrowserProfilePreparationResult> PrepareAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default);

    Task RecordLaunchResultAsync(
        Guid bindingId,
        bool succeeded,
        CancellationToken cancellationToken = default);
}

public interface IBrowserProfilePromotionService
{
    Task<BrowserProfilePromotionResult> PromoteCandidateAsync(
        Guid accountId,
        BrowserProfileSessionContext candidate,
        CancellationToken cancellationToken = default);
}
