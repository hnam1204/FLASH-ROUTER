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
public sealed class AccountBrowserSessionManager : IAccountBrowserSessionManager, IAccountBrowserSessionRegistration, IDisposable
{
    private const string ProviderId = "openai-chatgpt";
    private readonly IBrowserProfileManager _profiles;
    private readonly IChromeProcessLauncher _launcher;
    private readonly IChromeProcessController? _controller;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IBrowserPageStateDetector _stateDetector;
    private readonly ConcurrentDictionary<Guid, RuntimeEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public AccountBrowserSessionManager(
        IBrowserProfileManager profiles,
        IChromeProcessLauncher launcher,
        IServiceScopeFactory? scopeFactory = null,
        IBrowserPageStateDetector? stateDetector = null)
    {
        _profiles = profiles;
        _launcher = launcher;
        _controller = launcher as IChromeProcessController;
        _scopeFactory = scopeFactory;
        _stateDetector = stateDetector ?? new StableBrowserPageStateDetector();
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
                var observation = state == BrowserPageState.Authenticated
                    ? new AuthenticationObservationResult(
                        accountId,
                        entry.AttemptId ?? Guid.NewGuid(),
                        entry.Context.ProviderId,
                        AuthenticationObservationStatus.AuthenticatedSessionConfirmed,
                        IdentityVerificationStatus.Unverified,
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
                    IdentityVerificationState = IdentityVerificationStatus.Unverified,
                    LastSessionObservedAtUtc = observation?.ObservedAtUtc,
                    LastAuthenticatedSessionConfirmedAtUtc = observation?.ObservedAtUtc,
                    EvidenceSource = "Official provider page-state observation"
                };
                if (observation is not null)
                {
                    await PersistAuthenticationAsync(observation);
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
            await AuditAsync(pair.Key, "BrowserDisconnected", "Browser process không còn chạy; authentication history vẫn được giữ nguyên.", "Warning", "BROWSER_DISCONNECTED");
            await AuditAsync(pair.Key, "BrowserCrashed", "Browser runtime đã kết thúc ngoài thao tác đóng.", "Warning", "BROWSER_DISCONNECTED");
        }
    }

    public Task RegisterManagedSessionAsync(
        Guid accountId,
        Guid attemptId,
        BrowserProfileSessionContext profile,
        IBrowserAuthenticationSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries[accountId] = new RuntimeEntry(profile, AccountBrowserRuntimeState.Open, DateTimeOffset.UtcNow, session, attemptId);
        return AuditAsync(accountId, "BrowserOpened", "Playwright context của attempt đã được liên kết với lifecycle manager.", "Success");
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
            bindingId ?? entry?.Context.BindingId,
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
            historical?.SanitizedFailureCode);

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

    private async Task PersistAuthenticationAsync(AuthenticationObservationResult observation)
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
                await store.RecordAsync(new AccountAuthenticationStateUpdate(
                    observation.AccountId,
                    observation.AttemptId,
                    observation.ProviderId,
                    "AuthenticatedSessionConfirmed",
                    observation.IdentityStatus.ToString(),
                    observation.ObservedAtUtc,
                    observation.ObservedAtUtc,
                    observation.ObservedAtUtc,
                    null,
                    observation.ObservedAtUtc,
                    observation.EvidenceSource,
                    observation.SanitizedFailureCode));
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
        public IBrowserAuthenticationSession? Session { get; set; }
        public Guid? AttemptId { get; }
    }
}
