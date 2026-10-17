using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using Microsoft.Extensions.Logging;

namespace FlashAI.Modules.Connections.Application;

public sealed class BrowserAuthOrchestrator : IBrowserAuthOrchestrator, IEphemeralBrowserAuthOrchestrator, IAsyncDisposable
{
    private static readonly TimeSpan AttemptLifetime = TimeSpan.FromMinutes(10);
    private readonly IBrowserProfileManager _profileManager;
    private readonly IBrowserAuthenticationSessionFactory _sessionFactory;
    private readonly IBrowserPageStateDetector _stateDetector;
    private readonly BrowserAuthCredentialReader _credentialReader;
    private readonly ILocalTotpCodeGenerator _totpGenerator;
    private readonly IVaultAccessState? _vaultAccess;
    private readonly Func<Uri?, bool> _isAllowedOrigin;
    private readonly IEphemeralBrowserProfileFactory? _ephemeralProfileFactory;
    private readonly ILogger<BrowserAuthOrchestrator>? _logger;
    private readonly ConcurrentDictionary<Guid, AttemptState> _attempts = new();
    private readonly ConcurrentDictionary<Guid, byte> _autoLoginAccounts = new();
    private int _ephemeralAttemptActive;

    public BrowserAuthOrchestrator(
        IBrowserProfileManager profileManager,
        IBrowserAuthenticationSessionFactory sessionFactory,
        IBrowserPageStateDetector stateDetector,
        BrowserAuthCredentialReader credentialReader,
        ILocalTotpCodeGenerator totpGenerator,
        Func<Uri?, bool>? isAllowedOrigin = null,
        ILogger<BrowserAuthOrchestrator>? logger = null,
        IVaultAccessState? vaultAccess = null,
        IEphemeralBrowserProfileFactory? ephemeralProfileFactory = null)
    {
        _profileManager = profileManager;
        _sessionFactory = sessionFactory;
        _stateDetector = stateDetector;
        _credentialReader = credentialReader;
        _totpGenerator = totpGenerator;
        _vaultAccess = vaultAccess;
        _isAllowedOrigin = isAllowedOrigin ?? BrowserOriginPolicy.IsAllowed;
        _logger = logger;
        _ephemeralProfileFactory = ephemeralProfileFactory;
    }

    public async Task<BrowserAuthPreflight> GetAutoLoginPreflightAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var preflightTimer = Stopwatch.StartNew();
        const string providerId = "openai-chatgpt";
        _logger?.LogInformation("BrowserAuthEvent {EventName} {AccountId}", "AutoLoginPreflightStarted", accountId);
        var credentialReadiness = await _credentialReader.CheckReadinessAsync(accountId, cancellationToken);
        var binding = await _profileManager.GetBindingAsync(accountId, providerId, cancellationToken);
        var historicalLaunchFailure = string.Equals(binding?.State, BrowserProfileState.LaunchFailed.ToString(), StringComparison.OrdinalIgnoreCase);
        var profileStatus = historicalLaunchFailure
            ? "LaunchFailed (lịch sử; có thể thử lại an toàn)"
            : binding?.State ?? "Sẵn sàng tạo profile được quản lý riêng cho tài khoản";
        var vaultUnlocked = _vaultAccess?.IsUnlocked ?? true;
        var isReady = credentialReadiness.IsReady
            && vaultUnlocked
            && BrowserOriginPolicy.IsOfficialChatGptStart(BrowserOriginPolicy.ChatGptUri);

        var status = !credentialReadiness.IsReady
            ? credentialReadiness.Status
            : isReady ? "AUTO_LOGIN_READY" : "AUTO_LOGIN_NOT_READY";
        var message = !credentialReadiness.IsReady
            ? credentialReadiness.Message
            : isReady
                ? "Có thể cấp quyền tạm thời cho một lần Auto Login trên website ChatGPT chính thức."
                : "Auto Login chưa sẵn sàng với cấu hình hiện tại.";

        _logger?.LogInformation(
            "BrowserAuthEvent {EventName} {AccountId} {State} {ErrorCategory}",
            credentialReadiness.Kind == BrowserAuthCredentialReadinessKind.VaultLocked
                ? "VaultLockedDetected"
                : "CredentialReadinessChanged",
            accountId,
            credentialReadiness.Kind,
            credentialReadiness.Status);
        _logger?.LogInformation(
            "BrowserAuthEvent {EventName} {AccountId} {State} {ErrorCategory}",
            "BrowserProfileStateRead",
            accountId,
            profileStatus,
            historicalLaunchFailure ? "HISTORICAL_LAUNCH_FAILED" : string.Empty);

        return new BrowserAuthPreflight(
            accountId,
            providerId,
            BrowserOriginPolicy.ChatGptUri.ToString(),
            MaskAccountEmail(credentialReadiness.AccountEmail ?? string.Empty),
            vaultUnlocked,
            credentialReadiness.IsReady,
            profileStatus,
            isReady,
            status,
            message,
            new BrowserAuthTimingSnapshot(
                PreflightDurationMs: Math.Max(0L, preflightTimer.ElapsedMilliseconds)),
            credentialReadiness.Kind,
            credentialReadiness.ConnectionId,
            credentialReadiness.CredentialReferenceId,
            BrowserProfileRetryAvailable: historicalLaunchFailure,
            BrowserProfileFailureStatus: historicalLaunchFailure ? "BrowserLaunchFailed" : null);
    }

    public Task<BrowserAuthResult> AutoLoginAsync(
        Guid accountId,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => AutoLoginAsync(accountId, credentialRotationAttested: false, progress: progress, cancellationToken: cancellationToken);

    public async Task<BrowserAuthResult> AutoLoginAsync(
        Guid accountId,
        bool credentialRotationAttested,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!credentialRotationAttested)
        {
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "CREDENTIAL_ROTATION_ATTESTATION_REQUIRED",
                "Hãy xác nhận trong FLASH ROUTER rằng password và TOTP đã được thay mới trước khi Auto Login.");
        }

        if (!_autoLoginAccounts.TryAdd(accountId, 0))
        {
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "AUTO_LOGIN_IN_PROGRESS",
                "Auto Login của tài khoản này đang chạy; không thể tạo attempt thứ hai.");
        }

        BrowserAuthResult? result = null;
        AttemptState? attempt = null;
        try
        {
            ReportProgress(progress, "OpeningBrowser", BrowserAuthState.BrowserStarting, "Đang mở Chrome bằng profile riêng của tài khoản.");
            result = await StartAsync(accountId, cancellationToken);
            if (!result.IsSuccess || result.AttemptId is not Guid attemptId || !_attempts.TryGetValue(attemptId, out attempt))
            {
                return attempt is null ? result : WithTiming(result, attempt);
            }

            attempt.Consent = new AutoLoginAttemptConsent(
                attempt.Attempt.AttemptId,
                attempt.Attempt.AccountId,
                attempt.Attempt.ProviderId,
                attempt.Attempt.BrowserProfileBindingId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.Add(AttemptLifetime))
            {
                CredentialRotationAttested = credentialRotationAttested
            };
            LogEvent(attempt, "AutoLoginConsentIssued");

            ReportProgress(progress, "CheckingExistingSession", result.State, "Đang kiểm tra phiên ChatGPT hiện tại.");
            for (var step = 0; step < 8; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.State == BrowserAuthState.BrowserSessionObserved)
                {
                    attempt.Consent.Consume();
                    LogEvent(attempt, "AutoLoginCompleted");
                    return WithTiming(result, attempt);
                }

                if (result.State is BrowserAuthState.HumanActionRequired
                    or BrowserAuthState.ExternalProviderRequired
                    or BrowserAuthState.Unknown
                    or BrowserAuthState.Failed
                    or BrowserAuthState.ProfileInUse
                    or BrowserAuthState.TimedOut
                    or BrowserAuthState.Cancelled)
                {
                    attempt.Consent.Revoke();
                    return WithTiming(result, attempt);
                }

                result = result.State switch
                {
                    BrowserAuthState.GuestLandingLoginAvailable => await RunAutoLoginEntryAsync(attempt, progress, cancellationToken),
                    BrowserAuthState.EmailRequired => await RunAutoCredentialStepAsync(attempt, BrowserAuthAction.FillEmail, "FillingEmail", "Đang điền email của tài khoản đang chọn.", progress, cancellationToken),
                    BrowserAuthState.PasswordRequired => await RunAutoCredentialStepAsync(attempt, BrowserAuthAction.FillPassword, "FillingPassword", "Đang xử lý bước mật khẩu từ Vault.", progress, cancellationToken),
                    BrowserAuthState.MfaRequired => await RunAutoCredentialStepAsync(attempt, BrowserAuthAction.FillTotp, "FillingTotp", "Đang tạo và điền mã xác thực hai bước cục bộ.", progress, cancellationToken),
                    _ => BrowserAuthResult.Failure(attempt.State, "AUTO_LOGIN_STEP_UNSUPPORTED", "Bước đăng nhập hiện tại không được hỗ trợ tự động.", attempt.Attempt.AttemptId)
                };

                if (!result.IsSuccess)
                {
                    attempt.Consent.Revoke();
                    return WithTiming(result, attempt);
                }

                ReportProgress(progress, "CheckingAuthenticatedSession", result.State, "Đang kiểm tra trạng thái phiên ChatGPT.");
            }

            attempt.Consent.Revoke();
            return WithTiming(BrowserAuthResult.Failure(
                BrowserAuthState.HumanActionRequired,
                "AUTO_LOGIN_STEP_LIMIT",
                "Auto Login đã dừng sau số bước an toàn cho phép; hãy tiếp quản trong Chrome.",
                attempt.Attempt.AttemptId), attempt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (attempt is not null)
            {
                attempt.Consent?.Revoke();
                await CancelAsync(attempt.Attempt.AttemptId, CancellationToken.None);
                return WithTiming(new BrowserAuthResult(true, BrowserAuthState.Cancelled, "CANCELLED", "Đã hủy Auto Login.", attempt.Attempt.AttemptId), attempt);
            }

            return new BrowserAuthResult(true, BrowserAuthState.Cancelled, "CANCELLED", "Đã hủy Auto Login.");
        }
        finally
        {
            if (attempt is not null)
            {
                LogTiming(attempt);
            }
            _autoLoginAccounts.TryRemove(accountId, out _);
        }
    }

    public async Task<BrowserAuthResult> AutoLoginAsync(
        EphemeralBrowserAuthRequest request,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_ephemeralProfileFactory is null)
        {
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "EPHEMERAL_MODE_UNAVAILABLE",
                "One-shot console mode chưa được cấu hình trong host.");
        }

        var credential = request.Credential;
        if (credential.Account is null || string.IsNullOrEmpty(credential.Password))
        {
            credential.Clear();
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "CREDENTIAL_CONTEXT_EMPTY",
                "Credential one-shot đã hết hạn hoặc không hợp lệ.");
        }

        if (Interlocked.CompareExchange(ref _ephemeralAttemptActive, 1, 0) != 0)
        {
            credential.Clear();
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "EPHEMERAL_LOGIN_IN_PROGRESS",
                "Một one-shot attempt khác đang chạy; hãy chờ attempt hiện tại kết thúc.");
        }

        BrowserAuthResult? result = null;
        AttemptState? attempt = null;
        try
        {
            ReportProgress(progress, "ValidatingInput", BrowserAuthState.NotStarted, "Credential đã được xác thực trong bộ nhớ; không tạo AccountProfile.");
            var prepared = await _ephemeralProfileFactory.PrepareAsync(request.SessionMode, cancellationToken);
            if (!prepared.IsSuccess || prepared.Context is null)
            {
                await prepared.DisposeAsync();
                return BrowserAuthResult.Failure(BrowserAuthState.Failed, prepared.Status, prepared.Message);
            }

            attempt = CreateEphemeralAttempt(credential, prepared);
            _attempts[attempt.Attempt.AttemptId] = attempt;
            LogEvent(attempt, "EphemeralAttemptStarted");
            ReportProgress(progress, "BrowserStarted", BrowserAuthState.BrowserStarting, "Chrome profile one-shot đã được tạo.", attempt.Attempt.AttemptId);
            ReportProgress(progress, "OpeningBrowser", BrowserAuthState.BrowserStarting, "Đang mở Chrome bằng profile one-shot được quản lý riêng.", attempt.Attempt.AttemptId);

            try
            {
                attempt.State = BrowserAuthState.Navigating;
                LogEvent(attempt, "OfficialNavigationStarted");
                attempt.Session = await _sessionFactory.OpenAsync(prepared.Context, BrowserOriginPolicy.ChatGptUri, cancellationToken);
                if (attempt.Session is IBrowserAuthenticationSessionDiagnostics diagnostics)
                {
                    attempt.Timing.Merge(diagnostics.Timing);
                }

                LogEvent(attempt, "ChromeLaunchSucceeded");
                LogEvent(attempt, "OfficialNavigationSucceeded");
                LogEvent(attempt, "OfficialChatGPTReached");
                result = await RefreshCoreAsync(attempt, cancellationToken);
            }
            catch (BrowserAuthSessionException exception)
            {
                LogEvent(attempt, "ChromeLaunchFailed", exception.SafeCode);
                await DisposeAttemptSessionAsync(attempt);
                _attempts.TryRemove(attempt.Attempt.AttemptId, out _);
                var failedProfileCleanup = attempt.ProfileCleanup;
                if (failedProfileCleanup is not null)
                {
                    await failedProfileCleanup.DisposeAsync();
                    attempt.ProfileCleanup = null;
                }
                result = WithTiming(BrowserAuthResult.Failure(
                    exception.SafeCode == "PROFILE_IN_USE" ? BrowserAuthState.ProfileInUse : BrowserAuthState.Failed,
                    exception.SafeCode,
                    exception.SafeMessage,
                    attempt.Attempt.AttemptId), attempt);
            }

            if (result is null || !result.IsSuccess)
            {
                return result ?? BrowserAuthResult.Failure(BrowserAuthState.Failed, "BROWSER_START_FAILED", "Không thể khởi chạy phiên Chrome one-shot.");
            }

            attempt.Consent = new AutoLoginAttemptConsent(
                attempt.Attempt.AttemptId,
                attempt.Attempt.AccountId,
                attempt.Attempt.ProviderId,
                attempt.Attempt.BrowserProfileBindingId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.Add(AttemptLifetime))
            {
                // The user supplied this one-shot credential directly; no Vault rotation attestation applies.
                CredentialRotationAttested = true
            };
            LogEvent(attempt, "EphemeralConsentIssued");

            for (var step = 0; step < 8; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.State == BrowserAuthState.BrowserSessionObserved)
                {
                    attempt.Consent.Consume();
                    LogEvent(attempt, "AutoLoginCompleted");
                    return WithTiming(result, attempt);
                }

                if (result.State is BrowserAuthState.HumanActionRequired
                    or BrowserAuthState.ExternalProviderRequired
                    or BrowserAuthState.Unknown
                    or BrowserAuthState.Failed
                    or BrowserAuthState.ProfileInUse
                    or BrowserAuthState.TimedOut
                    or BrowserAuthState.Cancelled)
                {
                    attempt.Consent.Revoke();
                    return WithTiming(result, attempt);
                }

                result = result.State switch
                {
                    BrowserAuthState.GuestLandingLoginAvailable => await RunAutoLoginEntryAsync(attempt, progress, cancellationToken),
                    BrowserAuthState.EmailRequired => await RunAutoCredentialStepAsync(attempt, BrowserAuthAction.FillEmail, "FillingEmail", "Đang điền email từ context one-shot.", progress, cancellationToken),
                    BrowserAuthState.PasswordRequired => await RunAutoCredentialStepAsync(attempt, BrowserAuthAction.FillPassword, "FillingPassword", "Đang điền mật khẩu từ context one-shot.", progress, cancellationToken),
                    BrowserAuthState.MfaRequired => await RunAutoCredentialStepAsync(attempt, BrowserAuthAction.FillTotp, "FillingTotp", "Đang tạo TOTP cục bộ từ context one-shot.", progress, cancellationToken),
                    _ => BrowserAuthResult.Failure(attempt.State, "AUTO_LOGIN_STEP_UNSUPPORTED", "Bước đăng nhập hiện tại không được hỗ trợ tự động.", attempt.Attempt.AttemptId)
                };

                if (!result.IsSuccess)
                {
                    attempt.Consent.Revoke();
                    return WithTiming(result, attempt);
                }

                ReportProgress(progress, "CheckingAuthenticatedSession", result.State, "Đang kiểm tra trạng thái phiên ChatGPT.");
            }

            attempt.Consent.Revoke();
            return WithTiming(BrowserAuthResult.Failure(
                BrowserAuthState.HumanActionRequired,
                "AUTO_LOGIN_STEP_LIMIT",
                "One-shot Auto Login đã dừng sau số bước an toàn cho phép; hãy tiếp quản trong Chrome.",
                attempt.Attempt.AttemptId), attempt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (attempt is not null)
            {
                attempt.Consent?.Revoke();
                await CancelAsync(attempt.Attempt.AttemptId, CancellationToken.None);
                return new BrowserAuthResult(true, BrowserAuthState.Cancelled, "CANCELLED", "Đã hủy one-shot Auto Login.", attempt.Attempt.AttemptId);
            }

            return new BrowserAuthResult(true, BrowserAuthState.Cancelled, "CANCELLED", "Đã hủy one-shot Auto Login.");
        }
        finally
        {
            credential.Clear();
            Interlocked.Exchange(ref _ephemeralAttemptActive, 0);
            if (attempt is not null)
            {
                LogTiming(attempt);
            }
        }
    }

    private static AttemptState CreateEphemeralAttempt(
        EphemeralCredentialContext credential,
        EphemeralBrowserProfilePreparationResult prepared)
    {
        var now = DateTimeOffset.UtcNow;
        return new AttemptState(
            new BrowserAuthAttempt(
                Guid.NewGuid(),
                Guid.Empty,
                "openai-chatgpt",
                prepared.Context!.BindingId,
                now,
                now.Add(AttemptLifetime),
                BrowserAuthState.BrowserStarting),
            credential.Account!,
            prepared.Context,
            new BrowserAuthTimingRecorder())
        {
            EphemeralCredential = credential,
            ProfileCleanup = prepared
        };
    }

    public async Task<BrowserAuthResult> StartAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return BrowserAuthResult.Failure(BrowserAuthState.Failed, "ACCOUNT_REQUIRED", "Không xác định được tài khoản cần hỗ trợ đăng nhập.");
        }

        var email = await _credentialReader.GetAccountEmailAsync(accountId, cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
        {
            return BrowserAuthResult.Failure(BrowserAuthState.Failed, "ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại hoặc đã được lưu trữ.");
        }

        var credentialReference = await _credentialReader.GetReferenceSnapshotAsync(accountId, cancellationToken);
        var timing = new BrowserAuthTimingRecorder();
        var profileTimer = BrowserAuthTimingRecorder.StartStage();
        BrowserProfilePreparationResult prepared;
        try
        {
            prepared = await _profileManager.PrepareAsync(accountId, cancellationToken: cancellationToken);
        }
        finally
        {
            timing.Complete(BrowserAuthTimingStage.ProfileLease, profileTimer);
        }
        if (!prepared.IsSuccess || prepared.Context is null)
        {
            var state = prepared.Status == "PROFILE_IN_USE" ? BrowserAuthState.ProfileInUse : BrowserAuthState.Failed;
            return BrowserAuthResult.Failure(state, prepared.Status, prepared.Message);
        }

        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var attempt = new AttemptState(
            new BrowserAuthAttempt(
                attemptId,
                accountId,
                prepared.Context.ProviderId,
                prepared.Context.BindingId,
                now,
                now.Add(AttemptLifetime),
                BrowserAuthState.BrowserStarting,
                credentialReference.ConnectionId,
                credentialReference.CredentialReferenceId),
            email,
            prepared.Context,
            timing);

        _attempts[attemptId] = attempt;
        LogEvent(attempt, "AttemptStarted");
        LogEvent(attempt, "BrowserProfileSelected");
        LogEvent(attempt, "BrowserProfileLeaseRequested");
        try
        {
            attempt.State = BrowserAuthState.Navigating;
            LogEvent(attempt, "OfficialNavigationStarted");
            attempt.Session = await _sessionFactory.OpenAsync(prepared.Context, BrowserOriginPolicy.ChatGptUri, cancellationToken);
            if (attempt.Session is IBrowserAuthenticationSessionDiagnostics diagnostics)
            {
                attempt.Timing.Merge(diagnostics.Timing);
            }
            LogEvent(attempt, "ProfileLeaseAcquired");
            LogEvent(attempt, "ChromeLaunchSucceeded");
            LogEvent(attempt, "OfficialNavigationSucceeded");
            LogEvent(attempt, "OfficialChatGPTReached");
            await _profileManager.RecordLaunchResultAsync(prepared.Context.BindingId, true, CancellationToken.None);
            return await RefreshCoreAsync(attempt, cancellationToken);
        }
        catch (BrowserAuthSessionException exception)
        {
            LogEvent(attempt, "ChromeLaunchFailed", exception.SafeCode);
            await DisposeAttemptSessionAsync(attempt);
            await RecordFailedLaunchAsync(prepared.Context.BindingId);
            _attempts.TryRemove(attemptId, out _);
            var state = exception.SafeCode == "PROFILE_IN_USE" ? BrowserAuthState.ProfileInUse : BrowserAuthState.Failed;
            return WithTiming(BrowserAuthResult.Failure(state, exception.SafeCode, exception.SafeMessage, attemptId), attempt);
        }
        catch (OperationCanceledException)
        {
            await DisposeAttemptSessionAsync(attempt);
            await RecordFailedLaunchAsync(prepared.Context.BindingId);
            _attempts.TryRemove(attemptId, out _);
            throw;
        }
        catch
        {
            await DisposeAttemptSessionAsync(attempt);
            await RecordFailedLaunchAsync(prepared.Context.BindingId);
            _attempts.TryRemove(attemptId, out _);
            return WithTiming(BrowserAuthResult.Failure(BrowserAuthState.Failed, "BROWSER_START_FAILED", "Không thể khởi chạy phiên Chrome hỗ trợ.", attemptId), attempt);
        }
    }

    public async Task<BrowserAuthResult> RefreshAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        if (!_attempts.TryGetValue(attemptId, out var attempt))
        {
            return BrowserAuthResult.Failure(BrowserAuthState.Failed, "ATTEMPT_NOT_FOUND", "Phiên hỗ trợ đăng nhập không còn tồn tại.", attemptId);
        }

        await attempt.Gate.WaitAsync(cancellationToken);
        try
        {
            return await RefreshCoreAsync(attempt, cancellationToken);
        }
        finally
        {
            attempt.Gate.Release();
        }
    }

    public async Task<BrowserAuthResult> ActivateLoginEntryAsync(
        Guid attemptId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!_attempts.TryGetValue(attemptId, out var attempt))
        {
            return BrowserAuthResult.Failure(BrowserAuthState.Failed, "ATTEMPT_NOT_FOUND", "Phiên hỗ trợ đăng nhập không còn tồn tại.", attemptId);
        }

        Stopwatch? ctaTimer = null;
        await attempt.Gate.WaitAsync(cancellationToken);
        try
        {
            if (IsExpired(attempt))
            {
                return await TimeoutAsync(attempt);
            }

            if (attempt.State != BrowserAuthState.GuestLandingLoginAvailable)
            {
                return WithTiming(BrowserAuthResult.Failure(attempt.State, "ACTION_NOT_ALLOWED", "Trang hiện tại không còn ở bước bắt đầu đăng nhập an toàn.", attempt.Attempt.AttemptId), attempt);
            }

            var consentFailure = ValidateAutoConsent(attempt, BrowserAuthAction.ActivateLoginEntry);
            if (consentFailure is not null)
            {
                return WithTiming(consentFailure, attempt);
            }

            if (!confirmed)
            {
                return WithTiming(new BrowserAuthResult(
                    true,
                    attempt.State,
                    "CONFIRMATION_REQUIRED",
                    "Cần xác nhận trước khi kích hoạt nút Đăng nhập công khai.",
                    attempt.Attempt.AttemptId,
                    BrowserAuthAction.ActivateLoginEntry,
                    MaskedAccountEmail: MaskAccountEmail(attempt.AccountEmail)), attempt);
            }

            if (!_isAllowedOrigin(attempt.Session?.CurrentUri))
            {
                return WithTiming(BrowserAuthResult.Failure(BrowserAuthState.Failed, "BROWSER_ORIGIN_UNTRUSTED", "Đã chặn điều hướng vì Chrome không còn ở origin chính thức.", attempt.Attempt.AttemptId), attempt);
            }

            ctaTimer = BrowserAuthTimingRecorder.StartStage();
            attempt.State = BrowserAuthState.Navigating;
            await attempt.Session!.ActivateLoginEntryAsync(cancellationToken);
            LogEvent(attempt, "LoginCtaActivated");
            return await RefreshCoreAsync(attempt, cancellationToken);
        }
        catch (BrowserAuthSessionException exception)
        {
            return MapSessionFailure(attempt, exception);
        }
        finally
        {
            if (ctaTimer is not null)
            {
                attempt.Timing.Complete(BrowserAuthTimingStage.LoginCta, ctaTimer);
            }
            attempt.Gate.Release();
        }
    }

    public async Task<BrowserAuthResult> ConfirmAndFillAsync(
        Guid attemptId,
        BrowserAuthAction action,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!_attempts.TryGetValue(attemptId, out var attempt))
        {
            return BrowserAuthResult.Failure(BrowserAuthState.Failed, "ATTEMPT_NOT_FOUND", "Phiên hỗ trợ đăng nhập không còn tồn tại.", attemptId);
        }

        Stopwatch? stepTimer = null;
        var timingStage = action switch
        {
            BrowserAuthAction.FillEmail => BrowserAuthTimingStage.EmailStep,
            BrowserAuthAction.FillPassword => BrowserAuthTimingStage.PasswordStep,
            BrowserAuthAction.FillTotp => BrowserAuthTimingStage.MfaStep,
            _ => (BrowserAuthTimingStage?)null
        };

        await attempt.Gate.WaitAsync(cancellationToken);
        try
        {
            if (IsExpired(attempt))
            {
                return await TimeoutAsync(attempt);
            }

            var expected = action switch
            {
                BrowserAuthAction.FillEmail => BrowserAuthState.EmailRequired,
                BrowserAuthAction.FillPassword => BrowserAuthState.PasswordRequired,
                BrowserAuthAction.FillTotp => BrowserAuthState.MfaRequired,
                _ => BrowserAuthState.Unknown
            };
            if (attempt.State != expected)
            {
                return WithTiming(BrowserAuthResult.Failure(attempt.State, "ACTION_NOT_ALLOWED", "Bước xác thực hiện tại không khớp với thao tác được yêu cầu.", attempt.Attempt.AttemptId), attempt);
            }

            var consentFailure = ValidateAutoConsent(attempt, action);
            if (consentFailure is not null)
            {
                return WithTiming(consentFailure, attempt);
            }

            if (!confirmed)
            {
                return WithTiming(new BrowserAuthResult(
                    true,
                    attempt.State,
                    "CONFIRMATION_REQUIRED",
                    "Cần xác nhận riêng trước khi điền thông tin nhạy cảm.",
                    attempt.Attempt.AttemptId,
                    action,
                    MaskedAccountEmail: MaskAccountEmail(attempt.AccountEmail)), attempt);
            }

            if (!_isAllowedOrigin(attempt.Session?.CurrentUri))
            {
                return WithTiming(BrowserAuthResult.Failure(BrowserAuthState.Failed, "BROWSER_ORIGIN_UNTRUSTED", "Đã chặn việc điền vì Chrome không còn ở origin chính thức.", attempt.Attempt.AttemptId), attempt);
            }

            if (timingStage is not null)
            {
                stepTimer = BrowserAuthTimingRecorder.StartStage();
            }

            if (action == BrowserAuthAction.FillEmail)
            {
                if (attempt.EmailFilled)
                {
                    return WithTiming(RepeatedFillBlocked(attempt), attempt);
                }

                LogEvent(attempt, "EmailActionApproved");
                await attempt.Session!.FillAsync(BrowserAuthField.Email, attempt.AccountEmail, cancellationToken);
                attempt.EmailFilled = true;
                LogEvent(attempt, "EmailFilled");
                await attempt.Session.ActivateEmailContinueAsync(cancellationToken);
                attempt.EmailContinueActivated = true;
                LogEvent(attempt, "EmailContinued");
                return await RefreshUntilEmailTransitionAsync(attempt, cancellationToken);
            }
            else
            {
                var credentialResult = await ReadCredentialAsync(attempt, cancellationToken);
                if (!credentialResult.IsSuccess || credentialResult.Credential is null)
                {
                    if (credentialResult.Status == "VAULT_LOCKED")
                    {
                        LogEvent(attempt, "VaultUnlockRequired", credentialResult.Status);
                    }

                    return WithTiming(BrowserAuthResult.Failure(attempt.State, credentialResult.Status, credentialResult.Message, attempt.Attempt.AttemptId), attempt);
                }

                if (action == BrowserAuthAction.FillPassword)
                {
                    if (attempt.PasswordFilled)
                    {
                        return WithTiming(RepeatedFillBlocked(attempt), attempt);
                    }

                    LogEvent(attempt, "PasswordActionApproved");
                    await attempt.Session!.FillAsync(BrowserAuthField.Password, credentialResult.Credential.Password, cancellationToken);
                    attempt.PasswordFilled = true;
                    await attempt.Session.ActivatePasswordContinueAsync(cancellationToken);
                    attempt.PasswordContinueActivated = true;
                    LogEvent(attempt, "PasswordSubmitted");
                    return await RefreshUntilCredentialTransitionAsync(attempt, cancellationToken);
                }
                else
                {
                    if (attempt.TotpFilled)
                    {
                        return WithTiming(RepeatedFillBlocked(attempt), attempt);
                    }

                    if (string.IsNullOrWhiteSpace(credentialResult.Credential.TotpSeed))
                    {
                        return WithTiming(BrowserAuthResult.Failure(BrowserAuthState.MfaRequired, "MFA_SEED_MISSING", "Vault không có TOTP seed cho tài khoản đang chọn.", attempt.Attempt.AttemptId), attempt);
                    }

                    byte[] secret;
                    try
                    {
                        secret = Base32SecretCodec.Decode(credentialResult.Credential.TotpSeed);
                    }
                    catch (FormatException)
                    {
                        return WithTiming(BrowserAuthResult.Failure(BrowserAuthState.MfaRequired, "TOTP_SEED_INVALID", "TOTP seed trong Vault không hợp lệ.", attempt.Attempt.AttemptId), attempt);
                    }

                    string code;
                    var now = DateTimeOffset.UtcNow;
                    try
                    {
                        code = _totpGenerator.Generate(secret, now, 6, 30);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(secret);
                    }

                    LogEvent(attempt, "TotpActionApproved");
                    await attempt.Session!.FillAsync(BrowserAuthField.Totp, code, cancellationToken);
                    attempt.TotpFilled = true;
                    await attempt.Session.ActivateTotpContinueAsync(cancellationToken);
                    attempt.TotpContinueActivated = true;
                    LogEvent(attempt, "TotpSubmitted");
                    var remaining = 30 - (int)(now.ToUnixTimeSeconds() % 30);
                    return await RefreshUntilCredentialTransitionAsync(attempt, cancellationToken, remaining);
                }
            }
        }
        catch (BrowserAuthSessionException exception)
        {
            return MapSessionFailure(attempt, exception);
        }
        finally
        {
            if (timingStage is not null && stepTimer is not null)
            {
                attempt.Timing.Complete(timingStage.Value, stepTimer);
            }
            attempt.Gate.Release();
        }
    }

    public async Task<BrowserAuthResult> CancelAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        if (!_attempts.TryRemove(attemptId, out var attempt))
        {
            return new BrowserAuthResult(true, BrowserAuthState.Cancelled, "ALREADY_CLOSED", "Phiên hỗ trợ đăng nhập đã được đóng.", attemptId);
        }

        await attempt.Gate.WaitAsync(cancellationToken);
        try
        {
            attempt.State = BrowserAuthState.Cancelled;
            attempt.Consent?.Revoke();
            _autoLoginAccounts.TryRemove(attempt.Attempt.AccountId, out _);
            if (attempt.Session is not null)
            {
                await attempt.Session.DisposeAsync();
                attempt.Session = null;
            }

            if (attempt.ProfileCleanup is not null)
            {
                await attempt.ProfileCleanup.DisposeAsync();
                attempt.ProfileCleanup = null;
            }

            LogEvent(attempt, "AttemptCancelled");
            LogEvent(attempt, "ProfileLeaseReleased");

            var result = new BrowserAuthResult(true, BrowserAuthState.Cancelled, "CANCELLED", "Đã hủy phiên hỗ trợ đăng nhập.", attemptId);
            LogTiming(attempt);
            return WithTiming(result, attempt);
        }
        finally
        {
            attempt.Gate.Release();
            attempt.Gate.Dispose();
        }
    }

    private async Task<BrowserAuthResult> RunAutoLoginEntryAsync(
        AttemptState attempt,
        IProgress<BrowserAuthProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, "OpeningLogin", BrowserAuthState.Navigating, "Đang mở biểu mẫu đăng nhập chính thức của ChatGPT.", attempt.Attempt.AttemptId);
        return await ActivateLoginEntryAsync(attempt.Attempt.AttemptId, confirmed: true, cancellationToken: cancellationToken);
    }

    private async Task<BrowserAuthResult> RunAutoCredentialStepAsync(
        AttemptState attempt,
        BrowserAuthAction action,
        string stage,
        string message,
        IProgress<BrowserAuthProgress>? progress,
        CancellationToken cancellationToken)
    {
        var state = action switch
        {
            BrowserAuthAction.FillEmail => BrowserAuthState.EmailRequired,
            BrowserAuthAction.FillPassword => BrowserAuthState.PasswordRequired,
            BrowserAuthAction.FillTotp => BrowserAuthState.MfaRequired,
            _ => BrowserAuthState.Unknown
        };
        ReportProgress(progress, stage, state, message, attempt.Attempt.AttemptId);
        return await ConfirmAndFillAsync(attempt.Attempt.AttemptId, action, confirmed: true, cancellationToken: cancellationToken);
    }

    private static BrowserAuthResult? ValidateAutoConsent(
        AttemptState attempt,
        BrowserAuthAction action)
    {
        var consent = attempt.Consent;
        if (consent is null)
        {
            return null;
        }

        if (consent.AttemptId != attempt.Attempt.AttemptId
            || consent.AccountId != attempt.Attempt.AccountId
            || !consent.ProviderId.Equals(attempt.Attempt.ProviderId, StringComparison.OrdinalIgnoreCase)
            || consent.BrowserProfileBindingId != attempt.Attempt.BrowserProfileBindingId)
        {
            consent.Revoke();
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "AUTO_LOGIN_CONSENT_SCOPE_INVALID",
                "Quyền Auto Login không còn khớp với tài khoản hoặc browser profile đang mở.",
                attempt.Attempt.AttemptId);
        }

        if (!consent.IsActive(DateTimeOffset.UtcNow))
        {
            consent.Revoke();
            return BrowserAuthResult.Failure(
                BrowserAuthState.TimedOut,
                "AUTO_LOGIN_CONSENT_EXPIRED",
                "Quyền Auto Login đã hết hạn; hãy bắt đầu một attempt mới.",
                attempt.Attempt.AttemptId);
        }

        if (action != BrowserAuthAction.ActivateLoginEntry && !consent.Allows(action))
        {
            consent.Revoke();
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "AUTO_LOGIN_ACTION_NOT_ALLOWED",
                "Hành động này không nằm trong phạm vi quyền Auto Login đã được cấp.",
                attempt.Attempt.AttemptId);
        }

        if (action is BrowserAuthAction.FillPassword or BrowserAuthAction.FillTotp
            && !consent.CredentialRotationAttested)
        {
            consent.Revoke();
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                "CREDENTIAL_ROTATION_ATTESTATION_REQUIRED",
                "Đã chặn thao tác nhạy cảm vì chưa xác nhận credential đã được thay mới.",
                attempt.Attempt.AttemptId);
        }

        return null;
    }

    private static void ReportProgress(
        IProgress<BrowserAuthProgress>? progress,
        string stage,
        BrowserAuthState state,
        string message,
        Guid? attemptId = null)
        => progress?.Report(new BrowserAuthProgress(
            stage,
            state,
            message,
            attemptId,
            EventCodeFor(stage, state),
            IsTerminal: IsTerminalState(state)));

    private static string EventCodeFor(string stage, BrowserAuthState state)
        => stage switch
        {
            "ValidatingInput" => "PARSE",
            "BrowserStarted" => "CHROMIUM_RESOLVE",
            "OpeningBrowser" => "BROWSER_LAUNCH",
            "OpeningLogin" => "LOGIN_ENTRY",
            "FillingEmail" => "EMAIL",
            "FillingPassword" => "PASSWORD",
            "FillingTotp" => "TOTP",
            "CheckingExistingSession" or "CheckingAuthenticatedSession" => "SESSION_OBSERVATION",
            _ when IsTerminalState(state) => "TERMINAL",
            _ => stage.ToUpperInvariant()
        };

    private static bool IsTerminalState(BrowserAuthState state)
        => state is BrowserAuthState.BrowserSessionObserved
            or BrowserAuthState.HumanActionRequired
            or BrowserAuthState.ExternalProviderRequired
            or BrowserAuthState.Failed
            or BrowserAuthState.ProfileInUse
            or BrowserAuthState.TimedOut
            or BrowserAuthState.Cancelled
            or BrowserAuthState.Unknown;

    private async Task<BrowserAuthResult> RefreshCoreAsync(
        AttemptState attempt,
        CancellationToken cancellationToken,
        int? totpRemainingSeconds = null)
    {
        if (IsExpired(attempt))
        {
            return await TimeoutAsync(attempt);
        }

        if (attempt.Session is null)
        {
            return WithTiming(BrowserAuthResult.Failure(BrowserAuthState.Failed, "BROWSER_SESSION_MISSING", "Phiên Chrome hỗ trợ không còn khả dụng.", attempt.Attempt.AttemptId), attempt);
        }

        var existingSessionTimer = attempt.ExistingSessionChecked ? null : BrowserAuthTimingRecorder.StartStage();
        var authenticatedObservationTimer = attempt.AuthenticatedObservationMeasured ? null : BrowserAuthTimingRecorder.StartStage();
        try
        {
            var snapshot = await attempt.Session.ReadPageAsync(cancellationToken);
            if (!_isAllowedOrigin(snapshot.CurrentUri))
            {
                attempt.State = BrowserAuthState.Failed;
                return WithTiming(BrowserAuthResult.Failure(attempt.State, "BROWSER_ORIGIN_UNTRUSTED", "Đã chặn phiên vì Chrome không còn ở origin chính thức.", attempt.Attempt.AttemptId), attempt);
            }

            var pageState = _stateDetector.Detect(snapshot);
            attempt.ExistingSessionChecked = true;
            if (pageState == BrowserPageState.Authenticated && authenticatedObservationTimer is not null)
            {
                attempt.Timing.Complete(BrowserAuthTimingStage.AuthenticatedObservation, authenticatedObservationTimer);
                attempt.AuthenticatedObservationMeasured = true;
            }
            attempt.State = pageState switch
            {
                BrowserPageState.GuestLandingLoginAvailable => BrowserAuthState.GuestLandingLoginAvailable,
                BrowserPageState.EmailRequired => BrowserAuthState.EmailRequired,
                BrowserPageState.PasswordRequired => BrowserAuthState.PasswordRequired,
                BrowserPageState.MfaRequired => BrowserAuthState.MfaRequired,
                BrowserPageState.ExternalProviderRequired => BrowserAuthState.ExternalProviderRequired,
                BrowserPageState.Authenticated => BrowserAuthState.BrowserSessionObserved,
                BrowserPageState.Captcha or BrowserPageState.Passkey or BrowserPageState.RecoveryCode or BrowserPageState.SmsOrEmailCode or BrowserPageState.RateLimited => BrowserAuthState.HumanActionRequired,
                _ => BrowserAuthState.Unknown
            };

            var result = CreateStateResult(attempt, pageState, totpRemainingSeconds);
            LogObservedState(attempt, snapshot, pageState);
            _logger?.LogInformation(
                "BrowserAuthAttemptState {AttemptId} {State} {Status}",
                attempt.Attempt.AttemptId,
                pageState,
                result.Status);
            return result;
        }
        catch (BrowserAuthSessionException exception)
        {
            return MapSessionFailure(attempt, exception);
        }
        finally
        {
            if (existingSessionTimer is not null)
            {
                attempt.Timing.Complete(BrowserAuthTimingStage.ExistingSessionCheck, existingSessionTimer);
                attempt.ExistingSessionChecked = true;
            }
        }
    }

    private static BrowserAuthResult CreateStateResult(
        AttemptState attempt,
        BrowserPageState pageState,
        int? totpRemainingSeconds)
    {
        var (status, message, action) = pageState switch
        {
            BrowserPageState.GuestLandingLoginAvailable => ("GUEST_LANDING_LOGIN_AVAILABLE", "Đã tìm thấy nút Đăng nhập trên website. Xác nhận để chuyển sang bước đăng nhập.", BrowserAuthAction.ActivateLoginEntry),
            BrowserPageState.EmailRequired => ("EMAIL_REQUIRED", "Đã nhận diện trường email ổn định trên origin chính thức. Xác nhận để điền email của tài khoản đang chọn.", BrowserAuthAction.FillEmail),
            BrowserPageState.PasswordRequired => ("PASSWORD_REQUIRED", "Đã nhận diện trường mật khẩu trên origin chính thức. Xác nhận để điền mật khẩu từ Vault.", BrowserAuthAction.FillPassword),
            BrowserPageState.MfaRequired => ("MFA_REQUIRED", "Đã nhận diện trường Authenticator. Xác nhận để tạo mã TOTP cục bộ và điền một lần.", BrowserAuthAction.FillTotp),
            BrowserPageState.ExternalProviderRequired => ("EXTERNAL_PROVIDER_REQUIRED", "Provider yêu cầu SSO hoặc nhà cung cấp bên ngoài. Hãy tiếp quản thao tác trong Chrome.", (BrowserAuthAction?)null),
            BrowserPageState.Captcha => ("CAPTCHA_REQUIRED", "Đã dừng để bạn tự hoàn tất CAPTCHA trong Chrome.", (BrowserAuthAction?)null),
            BrowserPageState.Passkey => ("PASSKEY_REQUIRED", "Đã dừng để bạn tự hoàn tất passkey hoặc security key trong Chrome.", (BrowserAuthAction?)null),
            BrowserPageState.RecoveryCode => ("RECOVERY_CODE_REQUIRED", "Đã dừng để bạn tự hoàn tất recovery code trong Chrome.", (BrowserAuthAction?)null),
            BrowserPageState.SmsOrEmailCode => ("HUMAN_CODE_REQUIRED", "Đã dừng để bạn tự hoàn tất mã SMS hoặc email trong Chrome.", (BrowserAuthAction?)null),
            BrowserPageState.RateLimited => ("RATE_LIMITED", "Website đang giới hạn yêu cầu. Hãy chờ và tiếp tục thủ công trong Chrome.", (BrowserAuthAction?)null),
            BrowserPageState.Authenticated => ("BROWSER_SESSION_OBSERVED", "Đã quan sát giao diện sau đăng nhập trong Chrome. Đây không phải OAuth hoặc Verified Identity.", (BrowserAuthAction?)null),
            _ => ("UNKNOWN_LOGIN_STATE", "Không nhận diện được bước đăng nhập an toàn. Hãy tiếp quản thao tác thủ công.", (BrowserAuthAction?)null)
        };

        var observation = pageState == BrowserPageState.Authenticated
            ? new BrowserSessionObservation(
                attempt.Attempt.AttemptId,
                attempt.Attempt.AccountId,
                attempt.Attempt.BrowserProfileBindingId,
                attempt.Attempt.ProviderId,
                DateTimeOffset.UtcNow,
                "Visible official-provider UI state",
                "Official HTTPS origin observed")
            : null;

        return new BrowserAuthResult(
            true,
            attempt.State,
            status,
            message,
            attempt.Attempt.AttemptId,
            action,
            totpRemainingSeconds,
            observation,
            MaskAccountEmail(attempt.AccountEmail),
            attempt.Timing.Snapshot());
    }

    private static BrowserAuthResult RepeatedFillBlocked(AttemptState attempt)
        => BrowserAuthResult.Failure(
            BrowserAuthState.HumanActionRequired,
            "REPEATED_FILL_BLOCKED",
            "Đã chặn việc điền lặp lại tự động. Hãy kiểm tra biểu mẫu và hoàn tất thao tác thủ công nếu cần.",
            attempt.Attempt.AttemptId);

    private BrowserAuthResult MapSessionFailure(AttemptState attempt, BrowserAuthSessionException exception)
    {
        attempt.State = exception.SafeCode == "PROFILE_IN_USE"
            ? BrowserAuthState.ProfileInUse
            : BrowserAuthState.Failed;
        if (exception.SafeCode == "BROWSER_CLOSED")
        {
            LogEvent(attempt, "BrowserClosed", exception.SafeCode);
        }

        LogEvent(attempt, "AttemptFailed", exception.SafeCode);
        return WithTiming(BrowserAuthResult.Failure(attempt.State, exception.SafeCode, exception.SafeMessage, attempt.Attempt.AttemptId), attempt);
    }

    private void LogObservedState(
        AttemptState attempt,
        BrowserPageSnapshot snapshot,
        BrowserPageState pageState)
    {
        if (attempt.LastObservedPageState == pageState)
        {
            return;
        }

        attempt.LastObservedPageState = pageState;
        var eventName = pageState switch
        {
            BrowserPageState.GuestLandingLoginAvailable => "GuestLandingDetected",
            BrowserPageState.EmailRequired when snapshot.IsEmailModal => "EmailModalDetected",
            BrowserPageState.PasswordRequired => "PasswordStepDetected",
            BrowserPageState.MfaRequired => "MfaStepDetected",
            BrowserPageState.Authenticated => "AuthenticatedUiObserved",
            BrowserPageState.Captcha or BrowserPageState.Passkey or BrowserPageState.RecoveryCode
                or BrowserPageState.SmsOrEmailCode or BrowserPageState.RateLimited or BrowserPageState.ExternalProviderRequired
                => "HumanTakeoverRequired",
            _ => null
        };

        if (eventName is not null)
        {
            LogEvent(attempt, eventName);
        }
    }

    private void LogEvent(AttemptState attempt, string eventName, string? errorCategory = null)
        => _logger?.LogInformation(
            "BrowserAuthEvent {EventName} {AttemptId} {AccountId} {BrowserProfileBindingId} {State} {ErrorCategory}",
            eventName,
            attempt.Attempt.AttemptId,
            attempt.Attempt.AccountId,
            attempt.Attempt.BrowserProfileBindingId,
            attempt.State,
            errorCategory ?? string.Empty);

    private static BrowserAuthResult WithTiming(BrowserAuthResult result, AttemptState attempt)
        => result with { Timing = attempt.Timing.Snapshot() };

    private void LogTiming(AttemptState attempt)
    {
        var timing = attempt.Timing.Snapshot();
        _logger?.Log(
            LogLevel.Information,
            new EventId(0, "BrowserAuthTiming"),
            new BrowserAuthTimingLogState(attempt.Attempt.AttemptId, attempt.Attempt.AccountId, attempt.Attempt.BrowserProfileBindingId, timing),
            exception: null,
            static (state, _) => $"BrowserAuthTiming {state}");
    }

    private sealed record BrowserAuthTimingLogState(
        Guid AttemptId,
        Guid AccountId,
        Guid BrowserProfileBindingId,
        BrowserAuthTimingSnapshot Timing)
    {
        public override string ToString()
            => $"{AttemptId} {AccountId} {BrowserProfileBindingId} {Timing}";
    }

    private static bool IsExpired(AttemptState attempt)
        => DateTimeOffset.UtcNow >= attempt.Attempt.ExpiresAtUtc;

    private static string MaskAccountEmail(string email)
    {
        var separator = email.IndexOf('@');
        if (separator <= 0 || separator == email.Length - 1)
        {
            return "***";
        }

        var visibleCharacters = Math.Min(separator, 2);
        return email[..visibleCharacters] + "***" + email[separator..];
    }

    private async Task<BrowserAuthResult> TimeoutAsync(AttemptState attempt)
    {
        attempt.State = BrowserAuthState.TimedOut;
        attempt.Consent?.Revoke();
        _autoLoginAccounts.TryRemove(attempt.Attempt.AccountId, out _);
        _attempts.TryRemove(attempt.Attempt.AttemptId, out _);
        if (attempt.Session is not null)
        {
            await attempt.Session.DisposeAsync();
            attempt.Session = null;
        }

        if (attempt.ProfileCleanup is not null)
        {
            await attempt.ProfileCleanup.DisposeAsync();
            attempt.ProfileCleanup = null;
        }

        LogEvent(attempt, "AttemptFailed", "ATTEMPT_TIMED_OUT");
        LogEvent(attempt, "ProfileLeaseReleased");
        LogTiming(attempt);

        return new BrowserAuthResult(
            false,
            BrowserAuthState.TimedOut,
            "ATTEMPT_TIMED_OUT",
            "Phiên hỗ trợ đăng nhập đã hết thời gian. Hãy mở lại từ tài khoản đang chọn.",
            attempt.Attempt.AttemptId,
            Timing: attempt.Timing.Snapshot());
    }

    private async Task RecordFailedLaunchAsync(Guid bindingId)
    {
        try
        {
            await _profileManager.RecordLaunchResultAsync(bindingId, false, CancellationToken.None);
        }
        catch
        {
            // Launch failures must not hide the safe browser error.
        }
    }

    private static async Task DisposeAttemptSessionAsync(AttemptState attempt)
    {
        if (attempt.Session is not null)
        {
            await attempt.Session.DisposeAsync();
            attempt.Session = null;
        }
    }

    private Task<StoredChatGptCredentialReadResult> ReadCredentialAsync(
        AttemptState attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.EphemeralCredential is { } ephemeral)
        {
            if (string.IsNullOrWhiteSpace(ephemeral.Account)
                || string.IsNullOrEmpty(ephemeral.Password))
            {
                return Task.FromResult(StoredChatGptCredentialReadResult.Failure(
                    "CREDENTIAL_CONTEXT_EMPTY",
                    "Credential one-shot không còn khả dụng.",
                    attempt.AccountEmail));
            }

            return Task.FromResult(new StoredChatGptCredentialReadResult(
                true,
                "CredentialReady",
                "Credential one-shot đang được dùng trong attempt hiện tại.",
                attempt.AccountEmail,
                new StoredChatGptCredential(ephemeral.Account, ephemeral.Password, ephemeral.TotpSeed)));
        }

        return _credentialReader.ReadAsync(attempt.Attempt.AccountId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pair in _attempts.ToArray())
        {
            if (_attempts.TryRemove(pair.Key, out var attempt))
            {
                await attempt.Gate.WaitAsync();
                try
                {
                    attempt.Consent?.Revoke();
                    _autoLoginAccounts.TryRemove(attempt.Attempt.AccountId, out _);
                    if (attempt.Session is not null)
                    {
                        await attempt.Session.DisposeAsync();
                        attempt.Session = null;
                    }

                    if (attempt.ProfileCleanup is not null)
                    {
                        await attempt.ProfileCleanup.DisposeAsync();
                        attempt.ProfileCleanup = null;
                    }

                    LogEvent(attempt, "ProfileLeaseReleased");
                    LogTiming(attempt);
                }
                finally
                {
                    attempt.Gate.Release();
                    attempt.Gate.Dispose();
                }
            }
        }
    }

    private sealed class AttemptState
    {
        public AttemptState(
            BrowserAuthAttempt attempt,
            string accountEmail,
            BrowserProfileSessionContext profile,
            BrowserAuthTimingRecorder timing)
        {
            Attempt = attempt;
            AccountEmail = accountEmail;
            Profile = profile;
            Timing = timing;
        }

        public BrowserAuthAttempt Attempt { get; }
        public string AccountEmail { get; }
        public BrowserProfileSessionContext Profile { get; }
        public BrowserAuthTimingRecorder Timing { get; }
        public BrowserAuthState State { get; set; }
        public IBrowserAuthenticationSession? Session { get; set; }
        public bool EmailFilled { get; set; }
        public bool EmailContinueActivated { get; set; }
        public bool PasswordFilled { get; set; }
        public bool PasswordContinueActivated { get; set; }
        public bool TotpFilled { get; set; }
        public bool TotpContinueActivated { get; set; }
        public BrowserPageState? LastObservedPageState { get; set; }
        public bool ExistingSessionChecked { get; set; }
        public bool AuthenticatedObservationMeasured { get; set; }
        public AutoLoginAttemptConsent? Consent { get; set; }
        public EphemeralCredentialContext? EphemeralCredential { get; set; }
        public EphemeralBrowserProfilePreparationResult? ProfileCleanup { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private async Task<BrowserAuthResult> RefreshUntilEmailTransitionAsync(
        AttemptState attempt,
        CancellationToken cancellationToken)
    {
        var result = await RefreshCoreAsync(attempt, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (result.State == BrowserAuthState.EmailRequired
            && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            result = await RefreshCoreAsync(attempt, cancellationToken);
        }

        return result;
    }

    private async Task<BrowserAuthResult> RefreshUntilCredentialTransitionAsync(
        AttemptState attempt,
        CancellationToken cancellationToken,
        int? totpRemainingSeconds = null)
    {
        var expectedState = attempt.State;
        var result = await RefreshCoreAsync(attempt, cancellationToken, totpRemainingSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (result.State == expectedState
            && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            result = await RefreshCoreAsync(attempt, cancellationToken, totpRemainingSeconds);
        }

        return result;
    }
}

public sealed class PreviewBrowserAuthOrchestrator : IBrowserAuthOrchestrator, IEphemeralBrowserAuthOrchestrator
{
    public Task<BrowserAuthPreflight> GetAutoLoginPreflightAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(new BrowserAuthPreflight(
            accountId,
            "openai-chatgpt",
            BrowserOriginPolicy.ChatGptUri.ToString(),
            null,
            false,
            false,
            "Preview mode",
            false,
            "PREVIEW_BROWSER_AUTH_DISABLED",
            "Preview mode không mở Chrome hoặc đọc Vault. Chuyển sang Integrated mode để dùng Auto Login."));

    public Task<BrowserAuthResult> AutoLoginAsync(Guid accountId, IProgress<BrowserAuthProgress>? progress = null, CancellationToken cancellationToken = default)
        => AutoLoginAsync(accountId, credentialRotationAttested: false, progress: progress, cancellationToken: cancellationToken);

    public Task<BrowserAuthResult> AutoLoginAsync(Guid accountId, bool credentialRotationAttested, IProgress<BrowserAuthProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(BrowserAuthResult.Failure(
            BrowserAuthState.Failed,
            "PREVIEW_BROWSER_AUTH_DISABLED",
            "Preview mode không mở Chrome hoặc đọc credential.",
            null));

    public Task<BrowserAuthResult> AutoLoginAsync(EphemeralBrowserAuthRequest request, IProgress<BrowserAuthProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        request.Credential.Clear();
        return Task.FromResult(BrowserAuthResult.Failure(
            BrowserAuthState.Failed,
            "PREVIEW_BROWSER_AUTH_DISABLED",
            "Preview mode không mở Chrome. Chuyển sang Integrated mode để dùng console one-shot."));
    }

    public Task<BrowserAuthResult> StartAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(BrowserAuthResult.Failure(
            BrowserAuthState.Failed,
            "PREVIEW_BROWSER_AUTH_DISABLED",
            "Preview mode không mở Chrome hoặc đọc Vault. Chuyển sang Integrated mode để dùng hỗ trợ đăng nhập an toàn."));

    public Task<BrowserAuthResult> RefreshAsync(Guid attemptId, CancellationToken cancellationToken = default)
        => Task.FromResult(BrowserAuthResult.Failure(BrowserAuthState.Failed, "PREVIEW_BROWSER_AUTH_DISABLED", "Preview mode không có browser attempt thật.", attemptId));

    public Task<BrowserAuthResult> ActivateLoginEntryAsync(Guid attemptId, bool confirmed, CancellationToken cancellationToken = default)
        => Task.FromResult(BrowserAuthResult.Failure(BrowserAuthState.Failed, "PREVIEW_BROWSER_AUTH_DISABLED", "Preview mode không có browser attempt thật.", attemptId));

    public Task<BrowserAuthResult> ConfirmAndFillAsync(Guid attemptId, BrowserAuthAction action, bool confirmed, CancellationToken cancellationToken = default)
        => Task.FromResult(BrowserAuthResult.Failure(BrowserAuthState.Failed, "PREVIEW_BROWSER_AUTH_DISABLED", "Preview mode không đọc credential hoặc điền browser.", attemptId));

    public Task<BrowserAuthResult> CancelAsync(Guid attemptId, CancellationToken cancellationToken = default)
        => Task.FromResult(new BrowserAuthResult(true, BrowserAuthState.Cancelled, "ALREADY_CLOSED", "Preview mode không có browser attempt thật.", attemptId));
}
