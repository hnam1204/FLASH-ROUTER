using System.Collections.Concurrent;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Connections.Application;

/// <summary>
/// Owns the runtime projection for account-bound managed Chrome processes.
/// Process handles never leave memory and are always addressed by the profile path
/// that was prepared for the requested account.
/// </summary>
public sealed class AccountBrowserSessionManager : IAccountBrowserSessionManager, IAccountBrowserSessionRegistration, IAccountDataObservationPort, IAuthorizedAccountDataObservationPort, IDisposable
{
    private const string ProviderId = "openai-chatgpt";
    private readonly IBrowserProfileManager _profiles;
    private readonly IChromeProcessLauncher _launcher;
    private readonly IChromeProcessController? _controller;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IBrowserPageStateDetector _stateDetector;
    private readonly IProviderAccountObservationStore? _accountObservationStore;
    private readonly ConcurrentDictionary<Guid, RuntimeEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public AccountBrowserSessionManager(
        IBrowserProfileManager profiles,
        IChromeProcessLauncher launcher,
        IServiceScopeFactory? scopeFactory = null,
        IBrowserPageStateDetector? stateDetector = null,
        IProviderAccountObservationStore? accountObservationStore = null)
    {
        _profiles = profiles;
        _launcher = launcher;
        _controller = launcher as IChromeProcessController;
        _scopeFactory = scopeFactory;
        _stateDetector = stateDetector ?? new StableBrowserPageStateDetector();
        _accountObservationStore = accountObservationStore;
    }

    public async Task<BrowserRuntimeSnapshot> GetStatusAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binding = await _profiles.GetBindingAsync(accountId, ProviderId, cancellationToken);
        var historical = await ReadAuthenticationAsync(accountId, cancellationToken);
        var entry = _entries.TryGetValue(accountId, out var current) ? current : null;
        if (entry is not null && IsGone(entry))
        {
            entry.RuntimeState = AccountBrowserRuntimeState.Crashed;
            entry.LastClosedAtUtc = DateTimeOffset.UtcNow;
            entry.CloseReason = BrowserCloseReason.BrowserProcessExited;
            await AuditAsync(accountId, "BrowserCrashed", "Chrome thuộc profile tài khoản đã dừng ngoài thao tác đóng.", "Warning", "BROWSER_EXITED");
        }

        return Snapshot(accountId, binding?.BindingId, entry?.Context.ProviderId ?? ProviderId, entry, historical);
    }

    public async Task<BrowserOperationResult> OpenAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return Failure(accountId, "ACCOUNT_REQUIRED", "Không xác định được tài khoản cần mở trình duyệt.");
        }

        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetStatusAsync(accountId, cancellationToken);
            if (current.RuntimeState == AccountBrowserRuntimeState.Open)
            {
                return new BrowserOperationResult(true, "ALREADY_OPEN", "Trình duyệt của tài khoản đã đang mở.", current);
            }

            await AuditAsync(accountId, "BrowserOpenRequested", "Đã yêu cầu mở trình duyệt theo AccountId.", "Info");
            var prepared = await _profiles.PrepareAsync(accountId, ProviderId, cancellationToken);
            if (!prepared.IsSuccess || prepared.Context is null)
            {
                var state = prepared.Status.Equals("PROFILE_IN_USE", StringComparison.OrdinalIgnoreCase)
                    ? AccountBrowserRuntimeState.ProfileInUse
                    : AccountBrowserRuntimeState.Unknown;
                var snapshot = await SnapshotForAsync(accountId, state, prepared.Status, cancellationToken);
                await AuditAsync(accountId, "BrowserOpenFailed", prepared.Message, "Warning", prepared.Status);
                return new BrowserOperationResult(false, prepared.Status, prepared.Message, snapshot);
            }

            var context = prepared.Context;
            var launching = new RuntimeEntry(context, AccountBrowserRuntimeState.Launching, DateTimeOffset.UtcNow);
            _entries[accountId] = launching;
            await AuditAsync(accountId, "BrowserLaunching", "Đang khởi chạy Chrome bằng profile riêng của tài khoản.", "Info");

            var launch = _launcher is IChromeProcessLauncherDiagnostics diagnostics
                ? diagnostics.TryLaunchDetailed(context.ChromeExecutablePath, context.ProfileDirectory, BrowserOriginPolicy.ChatGptUri.ToString())
                : (_launcher.TryLaunch(context.ChromeExecutablePath, context.ProfileDirectory, BrowserOriginPolicy.ChatGptUri.ToString(), out var error)
                    ? new ChromeLaunchResult(true, "BROWSER_OPENED", "Đã mở Chrome bằng profile riêng của tài khoản.")
                    : new ChromeLaunchResult(false, "BROWSER_OPEN_FAILED", error ?? "Không thể mở Chrome."));

            if (!launch.IsSuccess)
            {
                _entries.TryRemove(accountId, out _);
                await _profiles.RecordLaunchResultAsync(context.BindingId, false, CancellationToken.None);
                var state = launch.Status.Equals("PROFILE_IN_USE", StringComparison.OrdinalIgnoreCase)
                    ? AccountBrowserRuntimeState.ProfileInUse
                    : AccountBrowserRuntimeState.Unknown;
                var snapshot = await SnapshotForAsync(accountId, state, launch.Status, cancellationToken);
                await AuditAsync(accountId, "BrowserOpenFailed", launch.Message, "Warning", launch.Status);
                return new BrowserOperationResult(false, launch.Status, launch.Message, snapshot);
            }

            launching.RuntimeState = AccountBrowserRuntimeState.Open;
            await _profiles.RecordLaunchResultAsync(context.BindingId, true, CancellationToken.None);
            await AuditAsync(accountId, "ProfileLeaseAcquired", "Đã giữ ownership cho profile của tài khoản.", "Info");
            await AuditAsync(accountId, "BrowserOpened", "Chrome đã mở bằng profile riêng của tài khoản.", "Success");
            var opened = await SnapshotForAsync(accountId, AccountBrowserRuntimeState.Open, "Managed Chrome process", cancellationToken);
            return new BrowserOperationResult(true, "BROWSER_OPENED", "Đã mở trình duyệt bằng profile riêng của tài khoản. Việc mở trình duyệt không xác nhận đăng nhập.", opened);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BrowserOperationResult> CloseAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var entry = _entries.TryGetValue(accountId, out var current) ? current : null;
            var status = await GetStatusAsync(accountId, cancellationToken);
            if (entry is null || status.RuntimeState is AccountBrowserRuntimeState.Closed or AccountBrowserRuntimeState.Crashed)
            {
                var closed = status with { RuntimeState = AccountBrowserRuntimeState.Closed, LastClosedAtUtc = status.LastClosedAtUtc ?? DateTimeOffset.UtcNow };
                return new BrowserOperationResult(true, "ALREADY_CLOSED", "Trình duyệt của tài khoản đã đóng.", closed);
            }

            await AuditAsync(accountId, "BrowserCloseRequested", "Đã yêu cầu đóng trình duyệt theo AccountId.", "Info");
            entry.RuntimeState = AccountBrowserRuntimeState.Closing;
            await AuditAsync(accountId, "BrowserClosing", "Đang đóng Chrome thuộc profile của tài khoản.", "Info");
            if (entry.Session is not null)
            {
                try
                {
                    await entry.Session.DisposeAsync();
                    entry.Session = null;
                }
                catch (BrowserAuthSessionException exception)
                {
                    entry.RuntimeState = AccountBrowserRuntimeState.Disconnected;
                    var disconnected = await SnapshotForAsync(accountId, entry.RuntimeState, exception.SafeCode, cancellationToken);
                    return new BrowserOperationResult(false, exception.SafeCode, exception.SafeMessage, disconnected);
                }

                entry.RuntimeState = AccountBrowserRuntimeState.Closed;
                entry.LastClosedAtUtc = DateTimeOffset.UtcNow;
                entry.CloseReason = BrowserCloseReason.UserRequested;
                await AuditAsync(accountId, "BrowserClosed", "Managed Playwright context của tài khoản đã đóng.", "Success");
                await AuditAsync(accountId, "ProfileLeaseReleased", "Ownership của profile tài khoản đã được giải phóng.", "Info");
                var managedClosed = await SnapshotForAsync(accountId, AccountBrowserRuntimeState.Closed, "Managed Playwright context", cancellationToken);
                return new BrowserOperationResult(true, "BROWSER_CLOSED", "Đã đóng context browser của đúng tài khoản được chọn.", managedClosed);
            }

            if (_controller is null)
            {
                entry.RuntimeState = AccountBrowserRuntimeState.Unknown;
                var unavailable = await SnapshotForAsync(accountId, entry.RuntimeState, "BROWSER_CONTROLLER_UNAVAILABLE", cancellationToken);
                return new BrowserOperationResult(false, "BROWSER_CONTROLLER_UNAVAILABLE", "Không có bộ điều khiển đóng Chrome an toàn cho profile này.", unavailable);
            }

            if (!_controller.TryClose(entry.Context.ProfileDirectory, out var error))
            {
                entry.RuntimeState = AccountBrowserRuntimeState.Closing;
                var closing = await SnapshotForAsync(accountId, entry.RuntimeState, "BROWSER_CLOSE_PENDING", cancellationToken);
                return new BrowserOperationResult(false, "BROWSER_CLOSE_PENDING", error ?? "Chrome chưa xác nhận yêu cầu đóng.", closing);
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (_controller.IsRunning(entry.Context.ProfileDirectory) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken);
            }

            if (_controller.IsRunning(entry.Context.ProfileDirectory))
            {
                entry.RuntimeState = AccountBrowserRuntimeState.Closing;
                var pending = await SnapshotForAsync(accountId, entry.RuntimeState, "BROWSER_CLOSE_TIMEOUT", cancellationToken);
                return new BrowserOperationResult(false, "BROWSER_CLOSE_TIMEOUT", "Chrome chưa đóng trong thời gian giới hạn; không kết luận đã đóng.", pending);
            }

            entry.RuntimeState = AccountBrowserRuntimeState.Closed;
            entry.LastClosedAtUtc = DateTimeOffset.UtcNow;
            entry.CloseReason = BrowserCloseReason.UserRequested;
            await AuditAsync(accountId, "BrowserClosed", "Chrome của tài khoản đã đóng.", "Success");
            await AuditAsync(accountId, "ProfileLeaseReleased", "Ownership của profile tài khoản đã được giải phóng.", "Info");
            var closedSnapshot = await SnapshotForAsync(accountId, AccountBrowserRuntimeState.Closed, "Account-scoped close", cancellationToken);
            return new BrowserOperationResult(true, "BROWSER_CLOSED", "Đã đóng trình duyệt của đúng tài khoản được chọn.", closedSnapshot);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BrowserOperationResult> ReopenAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await AuditAsync(accountId, "BrowserReopenRequested", "Đã yêu cầu mở lại profile hiện có của tài khoản.", "Info");
        var closed = await CloseAsync(accountId, cancellationToken);
        if (!closed.IsSuccess && closed.Status is not "ALREADY_CLOSED")
        {
            return closed;
        }

        return await OpenAsync(accountId, cancellationToken);
    }

    public async Task<SessionObservationResult> CheckSessionAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await CheckSessionCoreAsync(accountId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SessionObservationResult> CheckSessionCoreAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(accountId, cancellationToken);
        if (status.RuntimeState != AccountBrowserRuntimeState.Open)
        {
            return new SessionObservationResult(false, "BROWSER_NOT_OPEN", "Không có browser runtime đang mở để kiểm tra phiên.", status);
        }

        await AuditAsync(accountId, "SessionCheckStarted", "Đang kiểm tra phiên hiện tại của browser thuộc tài khoản.", "Info");
        var entry = _entries.TryGetValue(accountId, out var current) ? current : null;
        if (entry?.Session is not null)
        {
            try
            {
                var page = await entry.Session.ReadPageAsync(cancellationToken);
                var state = _stateDetector.Detect(page);
                var historical = await ReadAuthenticationAsync(accountId, cancellationToken);
                var sameAttemptIdentity = entry.AttemptId is Guid activeAttemptId
                    && historical?.AttemptId == activeAttemptId
                    && Enum.TryParse<IdentityVerificationStatus>(historical.IdentityVerificationState, out var historicalIdentity)
                    && historicalIdentity == IdentityVerificationStatus.IdentityVerified
                        ? IdentityVerificationStatus.IdentityVerified
                        : IdentityVerificationStatus.Unverified;
                var observation = state == BrowserPageState.Authenticated
                    ? new AuthenticationObservationResult(
                        accountId,
                        entry.AttemptId ?? Guid.NewGuid(),
                        entry.Context.ProviderId,
                        AuthenticationObservationStatus.AuthenticatedSessionConfirmed,
                        sameAttemptIdentity,
                        DateTimeOffset.UtcNow,
                        "Official provider page-state observation",
                        null)
                    : null;
                var authState = state == BrowserPageState.Authenticated
                    ? AuthenticationObservationStatus.AuthenticatedSessionConfirmed
                    : state is BrowserPageState.GuestLandingLoginAvailable or BrowserPageState.EmailRequired or BrowserPageState.PasswordRequired
                        ? AuthenticationObservationStatus.LoginRequired
                        : AuthenticationObservationStatus.Unknown;
                var fresh = status with
                {
                    AuthenticationState = authState,
                    IdentityVerificationState = sameAttemptIdentity,
                    LastSessionObservedAtUtc = observation?.ObservedAtUtc,
                    LastAuthenticatedSessionConfirmedAtUtc = observation?.ObservedAtUtc,
                    EvidenceSource = "Official provider page-state observation"
                };
                if (observation is not null)
                {
                    await PersistAuthenticationAsync(observation, attemptedAtUtc: entry.StartedAtUtc);
                    await AuditAsync(accountId, "BrowserSessionObserved", "Đã quan sát UI chính thức của provider trong context thuộc tài khoản.", "Success");
                    await AuditAsync(accountId, "AuthenticatedSessionConfirmed", "Đã xác nhận trạng thái authenticated của provider; identity vẫn độc lập.", "Success");
                }

                var observed = new SessionObservationResult(observation is not null, observation is not null ? "AUTHENTICATED_SESSION_CONFIRMED" : "SESSION_NOT_CONFIRMED", observation is not null ? "Đã xác nhận phiên qua UI provider chính thức." : "Browser còn mở nhưng trang hiện tại chưa chứng minh phiên authenticated.", fresh, observation);
                await AuditAsync(accountId, "SessionCheckCompleted", observed.Message, observation is not null ? "Success" : "Warning", observed.Status);
                return observed;
            }
            catch (BrowserAuthSessionException exception)
            {
                entry.RuntimeState = AccountBrowserRuntimeState.Disconnected;
                var disconnected = await SnapshotForAsync(accountId, entry.RuntimeState, exception.SafeCode, cancellationToken);
                await AuditAsync(accountId, "SessionCheckFailed", exception.SafeMessage, "Warning", exception.SafeCode);
                return new SessionObservationResult(false, exception.SafeCode, exception.SafeMessage, disconnected);
            }
        }

        // The managed-Chrome UI process is intentionally not inspected through
        // private endpoints or copied session data. An external window has no
        // Playwright context available to this manager, so auth remains unknown.
        var result = new SessionObservationResult(
            false,
            "SESSION_OBSERVATION_UNAVAILABLE",
            "Browser đang mở nhưng chưa có context quản lý để quan sát trang provider; không kết luận đăng nhập.",
            status with
            {
                AuthenticationState = AuthenticationObservationStatus.Unknown,
                EvidenceSource = "External managed Chrome process; no inspectable context"
            });
        await AuditAsync(accountId, "SessionCheckCompleted", result.Message, "Warning", result.Status);
        return result;
    }

    public async Task<AccountObservationResult> ObserveAccountDataAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
        => await ObserveAccountDataAsync(
            accountId,
            AccountObservationPersistenceMode.PersistNonSecretObservations,
            cancellationToken);

    public async Task<AccountObservationResult> ObserveAccountDataAsync(
        Guid accountId,
        AccountObservationPersistenceMode persistenceMode,
        CancellationToken cancellationToken = default,
        bool readChatGptUsageApi = false)
    {
        var persistObservation = persistenceMode == AccountObservationPersistenceMode.PersistNonSecretObservations;
        var persistUsageSnapshotOnly = readChatGptUsageApi
            && persistenceMode == AccountObservationPersistenceMode.PersistChatGptUsageSnapshotOnly;
        var localObservationPersisted = false;
        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var status = await GetStatusAsync(accountId, cancellationToken);
            if (status.RuntimeState != AccountBrowserRuntimeState.Open)
            {
                return new AccountObservationResult(
                    false,
                    "BROWSER_NOT_OPEN",
                    "Chưa có phiên trình duyệt đang mở để kiểm tra dữ liệu tài khoản. Hãy mở phiên đã được quản lý hoặc yêu cầu đăng nhập lại; hệ thống không tự gửi credential/TOTP.",
                    status with { SanitizedFailureCode = "BROWSER_NOT_OPEN" });
            }

            if (!_entries.TryGetValue(accountId, out var entry)
                || entry.Session is not IBrowserAccountObservationSession observationSession)
            {
                return new AccountObservationResult(
                    false,
                    "ACCOUNT_SETTINGS_UNAVAILABLE",
                    "Không có context Playwright được sở hữu để đọc account/settings UI; cửa sổ Chrome ngoài không được đọc qua private state.",
                    status with { SanitizedFailureCode = "ACCOUNT_SETTINGS_UNAVAILABLE" });
            }

            var intendedEmail = await ReadAccountEmailAsync(accountId, cancellationToken);
            if (string.IsNullOrWhiteSpace(intendedEmail))
            {
                return new AccountObservationResult(
                    false,
                    "ACCOUNT_NOT_FOUND",
                    "Không thể xác định email đã lưu của account để so sánh identity.",
                    status with { SanitizedFailureCode = "ACCOUNT_NOT_FOUND" });
            }

            var attemptId = entry.AttemptId ?? Guid.NewGuid();
            await AuditAsync(accountId, "AccountObservationStarted", "Đã bắt đầu kiểm tra dữ liệu account từ UI provider chính thức.", "Info");
            ProviderAccountObservation observation;
            try
            {
                observation = await observationSession.ReadAccountObservationAsync(
                    accountId,
                    attemptId,
                    entry.Context.BindingId,
                    cancellationToken);
            }
            catch (BrowserAuthSessionException exception)
            {
                if (exception.SafeCode == "BROWSER_CLOSED")
                {
                    entry.RuntimeState = AccountBrowserRuntimeState.Disconnected;
                }

                var failedSnapshot = await SnapshotForAsync(accountId, entry.RuntimeState, exception.SafeCode, cancellationToken);
                await AuditAsync(accountId, "AccountObservationFailed", exception.SafeMessage, "Warning", exception.SafeCode);
                return new AccountObservationResult(
                    false,
                    exception.SafeCode,
                    exception.SafeMessage,
                    failedSnapshot,
                    IdentityStatus: IdentityVerificationStatus.Unknown);
            }

            var verifier = new ProviderAccountVerificationService();
            var identity = verifier.VerifyObservation(
                new ProviderIdentityVerificationRequest(
                    accountId,
                    attemptId,
                    entry.Context.BindingId,
                    entry.Context.ProviderId,
                    intendedEmail,
                    entry.Session!),
                observation);

            if (identity.SafeReasonCode == "IDENTITY_OBSERVATION_SCOPE_MISMATCH")
            {
                var scopeFailure = await SnapshotForAsync(
                    accountId,
                    entry.RuntimeState,
                    identity.SafeReasonCode,
                    cancellationToken);
                await AuditAsync(
                    accountId,
                    "AccountObservationFailed",
                    "Đã chặn observation vì context provider không khớp account/attempt/profile đang sở hữu.",
                    "Warning",
                    identity.SafeReasonCode);

                return new AccountObservationResult(
                    false,
                    identity.SafeReasonCode,
                    "Đã chặn dữ liệu provider vì context không khớp account đang kiểm tra; không lưu observation.",
                    scopeFailure,
                    IdentityStatus: IdentityVerificationStatus.Unverified);
            }

            if (readChatGptUsageApi)
            {
                observation = await AddAuthorizedUsageObservationAsync(
                    accountId,
                    entry,
                    observation,
                    identity.Status,
                    cancellationToken);
            }

            var usageApiSucceeded = readChatGptUsageApi
                && identity.Status == IdentityVerificationStatus.IdentityVerified
                && observation.UsageQuota.Status == ProviderAccountObservationFieldStatus.Observed;
            if (_accountObservationStore is not null
                && (persistObservation || persistUsageSnapshotOnly && usageApiSucceeded))
            {
                var persistedObservation = persistUsageSnapshotOnly
                    ? CreateUsageOnlyObservation(observation)
                    : observation;
                await _accountObservationStore.SaveAsync(persistedObservation, cancellationToken);
                localObservationPersisted = true;
            }

            if (persistObservation || persistUsageSnapshotOnly)
            {
                await PersistAuthenticationAsync(
                    new AuthenticationObservationResult(
                        accountId,
                        attemptId,
                        entry.Context.ProviderId,
                        observation.AuthenticatedShellDetected
                            ? AuthenticationObservationStatus.AuthenticatedSessionConfirmed
                            : AuthenticationObservationStatus.Unknown,
                        identity.Status,
                        observation.ObservedAtUtc,
                        observation.EvidenceSurface,
                        identity.SafeReasonCode,
                        identity.ObservedProviderEmail,
                        observation.EvidenceSurface,
                        identity.SafeReasonCode),
                    identity,
                    entry.StartedAtUtc);
            }

            var fresh = await SnapshotForAsync(accountId, entry.RuntimeState, observation.EvidenceSurface, cancellationToken);
            var resultStatus = usageApiSucceeded
                ? "CHATGPT_USAGE_API_OBSERVED"
                : readChatGptUsageApi && identity.Status == IdentityVerificationStatus.IdentityVerified
                    ? observation.UsageQuota.FailureReasonCode ?? "CHATGPT_USAGE_API_FAILED"
                    : ResolveObservationStatus(observation, identity);
            var resultMessage = resultStatus switch
            {
                "ACCOUNT_DATA_OBSERVED" => "Đã đọc dữ liệu account hiển thị trong UI provider; không thay đổi cài đặt nào.",
                "CHATGPT_USAGE_API_OBSERVED" => "Đã đọc plan/usage thật từ endpoint ChatGPT trong session account đã xác minh.",
                "IDENTITY_EMAIL_MISMATCH" => "Email provider khác email đã lưu; dữ liệu gốc và profile chính được giữ nguyên.",
                "IDENTITY_EMAIL_UNAVAILABLE" => "Đã quan sát phiên provider nhưng chưa có email đủ tin cậy để đối chiếu; không xác nhận danh tính.",
                "IDENTITY_EVIDENCE_INSUFFICIENT" => "Đã quan sát phiên provider nhưng bằng chứng identity chưa đủ mạnh để xác nhận.",
                "USAGE_API_UNAUTHORIZED" => "Session ChatGPT chưa đăng nhập hoặc đã hết hạn; chưa lấy được usage thật.",
                "USAGE_API_FORBIDDEN" => "ChatGPT từ chối quyền đọc usage cho session hiện tại.",
                "USAGE_API_RATE_LIMITED" => "ChatGPT giới hạn yêu cầu usage; hãy thử lại sau.",
                "USAGE_API_SCHEMA_UNSUPPORTED" => "Endpoint đã phản hồi nhưng chưa có field usage được hỗ trợ; không tạo số liệu suy đoán.",
                _ => "Đã lưu observation một phần; provider chưa hiển thị đủ dữ liệu được hỗ trợ."
            };
            var isSuccessfulObservation = resultStatus is "ACCOUNT_DATA_OBSERVED" or "CHATGPT_USAGE_API_OBSERVED"
                && identity.Status == IdentityVerificationStatus.IdentityVerified;
            await AuditAsync(
                accountId,
                isSuccessfulObservation ? "AccountObservationCompleted" : "AccountObservationPartial",
                resultMessage,
                isSuccessfulObservation ? "Success" : "Warning",
                resultStatus);
            return new AccountObservationResult(
                isSuccessfulObservation,
                resultStatus,
                resultMessage,
                fresh,
                ToSnapshot(observation),
                identity.Status,
                localObservationPersisted);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderAccountObservation> AddAuthorizedUsageObservationAsync(
        Guid accountId,
        RuntimeEntry entry,
        ProviderAccountObservation observation,
        IdentityVerificationStatus identityStatus,
        CancellationToken cancellationToken)
    {
        const string source = "GET https://chatgpt.com/backend-api/wham/usage";
        ChatGptUsageApiReadResult result;
        if (identityStatus != IdentityVerificationStatus.IdentityVerified)
        {
            result = new ChatGptUsageApiReadResult(
                false,
                "USAGE_IDENTITY_NOT_VERIFIED",
                "Đã bỏ qua endpoint usage vì email provider chưa được xác minh cho account này.");
        }
        else if (entry.Session is not IChatGptUsageApiObservationSession usageSession)
        {
            result = new ChatGptUsageApiReadResult(
                false,
                "USAGE_API_CONTEXT_UNAVAILABLE",
                "Session browser hiện tại không có context được hỗ trợ để đọc usage an toàn.");
        }
        else
        {
            if (entry.RuntimeState != AccountBrowserRuntimeState.Open
                || entry.Context.ProviderId != ProviderId
                || entry.Context.AccountId != accountId)
            {
                result = new ChatGptUsageApiReadResult(
                    false,
                    "USAGE_API_CONTEXT_SCOPE_MISMATCH",
                    "Đã chặn usage request vì account/provider không khớp browser context hiện tại.");
            }
            else
            {
                try
                {
                    result = await usageSession.ReadUsageApiAsync(cancellationToken);
                }
                catch (BrowserAuthSessionException exception)
                {
                    if (exception.SafeCode == "BROWSER_CLOSED")
                    {
                        entry.RuntimeState = AccountBrowserRuntimeState.Disconnected;
                    }

                    result = new ChatGptUsageApiReadResult(false, exception.SafeCode, exception.SafeMessage);
                }
            }
        }

        var observedAt = result.Snapshot?.ObservedAtUtc ?? DateTimeOffset.UtcNow;
        if (result.IsSuccess && result.Snapshot is not null)
        {
            var value = ChatGptWebUsageSnapshotCodec.Serialize(result.Snapshot);
            var usage = new ProviderAccountFieldEvidence(
                ProviderAccountObservationField.UsageQuota,
                value,
                source,
                "ChatGptWebUsageV1",
                "Read-only response from the account-bound authenticated ChatGPT browser context.",
                ProviderAccountObservationFieldStatus.Observed,
                observedAt);
            var plan = observation.Plan;
            if (result.Snapshot.PlanType is not null && plan.Status != ProviderAccountObservationFieldStatus.Observed)
            {
                plan = new ProviderAccountFieldEvidence(
                    ProviderAccountObservationField.Plan,
                    result.Snapshot.PlanType,
                    source,
                    "ChatGptWebPlanTypeV1",
                    "Plan type returned by the authenticated ChatGPT usage endpoint.",
                    ProviderAccountObservationFieldStatus.Observed,
                    observedAt);
            }

            await AuditAsync(accountId, "ChatGptUsageApiObserved", "Đã đọc usage qua GET read-only trong session account đã xác minh.", "Success");
            return observation with { Plan = plan, UsageQuota = usage, ObservationStatus = "Observed" };
        }

        var failedUsage = new ProviderAccountFieldEvidence(
            ProviderAccountObservationField.UsageQuota,
            null,
            source,
            "ChatGptWebUsageV1",
            result.Message,
            ProviderAccountObservationFieldStatus.Failed,
            observedAt,
            result.Status);
        await AuditAsync(accountId, "ChatGptUsageApiFailed", "Không thể đọc usage từ session ChatGPT hiện tại.", "Warning", result.Status);
        return observation with { UsageQuota = failedUsage, ObservationStatus = "PartiallyResolved" };
    }

    private static ProviderAccountObservation CreateUsageOnlyObservation(ProviderAccountObservation observation)
    {
        var observedAt = observation.UsageQuota.ObservedAtUtc;
        ProviderAccountFieldEvidence Hidden(ProviderAccountObservationField field)
            => new(
                field,
                null,
                "Không lưu trường này",
                "UsageSnapshotOnly",
                "Không thuộc dữ liệu quota đã chọn",
                ProviderAccountObservationFieldStatus.Unavailable,
                observedAt);
        var identityBinding = new ProviderAccountFieldEvidence(
            ProviderAccountObservationField.Email,
            null,
            "Phiên ChatGPT đã đối chiếu với account đang chọn",
            "VerifiedChatGptUsageBindingV1",
            "Đã khớp account",
            ProviderAccountObservationFieldStatus.Observed,
            observedAt);

        return observation with
        {
            ObservedAtUtc = observedAt,
            ObservationStatus = "UsageObserved",
            EvidenceSurface = "Phiên ChatGPT đã xác minh",
            FailureReasonCode = null,
            DisplayName = Hidden(ProviderAccountObservationField.DisplayName),
            Email = identityBinding,
            Plan = Hidden(ProviderAccountObservationField.Plan),
            MfaConfiguration = Hidden(ProviderAccountObservationField.MfaConfiguration)
        };
    }

    private static string ResolveObservationStatus(
        ProviderAccountObservation observation,
        ProviderIdentityVerificationResult identity)
    {
        if (identity.Status == IdentityVerificationStatus.Mismatch)
        {
            return "IDENTITY_EMAIL_MISMATCH";
        }

        if (identity.Status != IdentityVerificationStatus.IdentityVerified)
        {
            return identity.SafeReasonCode switch
            {
                "IDENTITY_EMAIL_NOT_EXPOSED" or "IDENTITY_EMAIL_UNAVAILABLE" => "IDENTITY_EMAIL_UNAVAILABLE",
                "IDENTITY_EVIDENCE_INSUFFICIENT" => "IDENTITY_EVIDENCE_INSUFFICIENT",
                _ => identity.SafeReasonCode ?? "IDENTITY_UNVERIFIED"
            };
        }

        return observation.FailureReasonCode
            ?? (observation.ObservationStatus.Equals("Observed", StringComparison.OrdinalIgnoreCase)
                ? "ACCOUNT_DATA_OBSERVED"
                : "ACCOUNT_SETTINGS_UNAVAILABLE");
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        foreach (var pair in _entries.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsGone(pair.Value) || pair.Value.RuntimeState is AccountBrowserRuntimeState.Closed or AccountBrowserRuntimeState.Crashed)
            {
                continue;
            }

            pair.Value.RuntimeState = AccountBrowserRuntimeState.Crashed;
            pair.Value.LastClosedAtUtc = DateTimeOffset.UtcNow;
            pair.Value.CloseReason = BrowserCloseReason.BrowserProcessExited;
            await AuditAsync(pair.Key, "BrowserDisconnected", "Browser process không còn chạy; authentication history vẫn được giữ nguyên.", "Warning", "BROWSER_DISCONNECTED");
            await AuditAsync(pair.Key, "BrowserCrashed", "Browser runtime đã kết thúc ngoài thao tác đóng.", "Warning", "BROWSER_DISCONNECTED");
        }
    }

    public async Task RegisterManagedSessionAsync(
        Guid accountId,
        Guid attemptId,
        BrowserProfileSessionContext profile,
        IBrowserAuthenticationSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_entries.TryGetValue(accountId, out var current)
                && current.AttemptId is Guid currentAttemptId
                && currentAttemptId != attemptId
                && current.RuntimeState is not (AccountBrowserRuntimeState.Closed or AccountBrowserRuntimeState.Crashed))
            {
                throw new BrowserAuthSessionException(
                    "STALE_ATTEMPT_REJECTED",
                    "Attempt cũ không được phép ghi đè browser session đang thuộc attempt mới hơn.");
            }

            _entries[accountId] = new RuntimeEntry(profile, AccountBrowserRuntimeState.Open, DateTimeOffset.UtcNow, session, attemptId);
        }
        finally
        {
            gate.Release();
        }

        await AuditAsync(accountId, "BrowserOpened", "Playwright context của attempt đã được liên kết với lifecycle manager.", "Success");
    }

    public async Task UnregisterManagedSessionAsync(
        Guid accountId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || attemptId == Guid.Empty)
        {
            return;
        }

        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!_entries.TryGetValue(accountId, out var entry) || entry.AttemptId != attemptId)
            {
                return;
            }

            _entries.TryRemove(accountId, out _);
            await AuditAsync(
                accountId,
                "BrowserSessionRegistrationReleased",
                "Đã tháo registration của attempt browser đã kết thúc; không thay đổi profile binding chính.",
                "Info",
                "SESSION_REGISTRATION_RELEASED");
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsGone(RuntimeEntry entry)
        => entry.Session is null
            && (entry.RuntimeState is AccountBrowserRuntimeState.Open or AccountBrowserRuntimeState.Launching or AccountBrowserRuntimeState.Closing)
            && _controller is not null
            && !_controller.IsRunning(entry.Context.ProfileDirectory);

    private async Task<BrowserRuntimeSnapshot> SnapshotForAsync(
        Guid accountId,
        AccountBrowserRuntimeState state,
        string evidence,
        CancellationToken cancellationToken)
    {
        var binding = await _profiles.GetBindingAsync(accountId, ProviderId, cancellationToken);
        var historical = await ReadAuthenticationAsync(accountId, cancellationToken);
        var entry = _entries.TryGetValue(accountId, out var current) ? current : null;
        if (entry is not null)
        {
            entry.RuntimeState = state;
            if (state == AccountBrowserRuntimeState.Crashed)
            {
                entry.CloseReason ??= BrowserCloseReason.Unknown;
            }
        }

        return Snapshot(accountId, binding?.BindingId, ProviderId, entry, historical, state, evidence);
    }

    private BrowserRuntimeSnapshot Snapshot(
        Guid accountId,
        Guid? bindingId,
        string providerId,
        RuntimeEntry? entry,
        AccountAuthenticationStateSnapshot? historical,
        AccountBrowserRuntimeState? overrideState = null,
        string? evidence = null)
        => new(
            accountId,
            // The active Playwright context owns the runtime binding. This is
            // important for one-shot candidates, whose binding is not the
            // persisted authoritative profile until promotion succeeds.
            entry?.Context.BindingId ?? bindingId,
            providerId,
            overrideState ?? (entry?.RuntimeState ?? AccountBrowserRuntimeState.Closed),
            Enum.TryParse<AuthenticationObservationStatus>(historical?.AuthenticationState, out var auth) ? auth : AuthenticationObservationStatus.NotChecked,
            Enum.TryParse<IdentityVerificationStatus>(historical?.IdentityVerificationState, out var identity) ? identity : IdentityVerificationStatus.NotChecked,
            DateTimeOffset.UtcNow,
            entry?.StartedAtUtc,
            entry?.LastClosedAtUtc,
            historical?.LastBrowserSessionObservedAtUtc,
            historical?.LastAuthenticatedSessionConfirmedAtUtc,
            evidence ?? historical?.EvidenceSource ?? "Current process reconciliation",
            historical?.SanitizedFailureCode,
            entry?.AttemptId,
            entry?.CloseReason);

    private async Task<AccountAuthenticationStateSnapshot?> ReadAuthenticationAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
        {
            return null;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetService<IAccountAuthenticationStateReader>();
        return reader is null ? null : await reader.GetAsync(accountId, cancellationToken);
    }

    private async Task<string?> ReadAccountEmailAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
        {
            return null;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetService<BrowserAuthCredentialReader>();
        return reader is null ? null : await reader.GetAccountEmailAsync(accountId, cancellationToken);
    }

    private static ProviderAccountObservationSnapshot ToSnapshot(ProviderAccountObservation observation)
        => new(
            Guid.NewGuid(),
            observation.AccountId,
            observation.AttemptId,
            observation.BrowserProfileBindingId,
            observation.ProviderId,
            observation.BrowserContextId,
            observation.ObservedAtUtc,
            observation.Locale,
            observation.AuthenticatedShellDetected,
            observation.AccountMenuDetected,
            observation.ObservationStatus,
            observation.EvidenceSurface,
            observation.FailureReasonCode,
            [observation.DisplayName, observation.Email, observation.Plan, observation.MfaConfiguration, observation.UsageQuota]);

    private async Task AuditAsync(Guid accountId, string eventType, string summary, string severity, string? errorCode = null)
    {
        if (_scopeFactory is null)
        {
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sink = scope.ServiceProvider.GetService<IAuditSink>();
            if (sink is not null)
            {
                await sink.WriteAsync(accountId, eventType, summary, severity, errorCode: errorCode, providerId: ProviderId, source: "BrowserSessionManager", outcome: severity.Equals("Success", StringComparison.OrdinalIgnoreCase) ? "confirmed" : "observed");
            }
        }
        catch
        {
            // Activity persistence must not change browser lifecycle outcome.
        }
    }

    private async Task PersistAuthenticationAsync(
        AuthenticationObservationResult observation,
        ProviderIdentityVerificationResult? identity = null,
        DateTimeOffset? attemptedAtUtc = null)
    {
        if (_scopeFactory is null)
        {
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetService<IAccountAuthenticationStateStore>();
            if (store is not null)
            {
                var confirmed = observation.Status == AuthenticationObservationStatus.AuthenticatedSessionConfirmed;
                await store.RecordAsync(new AccountAuthenticationStateUpdate(
                    observation.AccountId,
                    observation.AttemptId,
                    observation.ProviderId,
                    observation.Status.ToString(),
                    observation.IdentityStatus.ToString(),
                    attemptedAtUtc ?? observation.ObservedAtUtc,
                    confirmed ? observation.ObservedAtUtc : null,
                    confirmed ? observation.ObservedAtUtc : null,
                    null,
                    observation.ObservedAtUtc,
                    observation.EvidenceSource,
                    observation.SanitizedFailureCode,
                    observation.ObservedProviderEmail,
                    identity?.EvidenceSource ?? observation.IdentityEvidenceSource,
                    identity?.SafeReasonCode ?? observation.IdentityReasonCode));
            }
        }
        catch
        {
            // A projection failure must not turn a truthful browser observation into a false failure.
        }
    }

    private static BrowserOperationResult Failure(Guid accountId, string status, string message)
        => new(false, status, message, new BrowserRuntimeSnapshot(
            accountId,
            null,
            ProviderId,
            AccountBrowserRuntimeState.Unknown,
            AuthenticationObservationStatus.NotChecked,
            IdentityVerificationStatus.NotChecked,
            DateTimeOffset.UtcNow,
            EvidenceSource: "Validation"));

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }
    }

    private sealed class RuntimeEntry
    {
        public RuntimeEntry(
            BrowserProfileSessionContext context,
            AccountBrowserRuntimeState runtimeState,
            DateTimeOffset startedAtUtc,
            IBrowserAuthenticationSession? session = null,
            Guid? attemptId = null)
        {
            Context = context;
            RuntimeState = runtimeState;
            StartedAtUtc = startedAtUtc;
            Session = session;
            AttemptId = attemptId;
        }

        public BrowserProfileSessionContext Context { get; }
        public AccountBrowserRuntimeState RuntimeState { get; set; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? LastClosedAtUtc { get; set; }
        public BrowserCloseReason? CloseReason { get; set; }
        public IBrowserAuthenticationSession? Session { get; set; }
        public Guid? AttemptId { get; }
    }
}
