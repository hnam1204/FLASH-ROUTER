using System.Collections.Concurrent;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Extensions.Logging;

namespace FlashAI.Modules.Connections.Application;

public interface ILiveProviderCertificationCoordinator
{
    Task<LiveProviderCertificationResult> CertifyAsync(
        LiveProviderCertificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LiveProviderCertificationCoordinator : ILiveProviderCertificationCoordinator, IDisposable
{
    private const string ChatGptProviderId = "openai-chatgpt";
    private static readonly TimeSpan DefaultObservationTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TransientObservationRetryWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TransientObservationRetryDelay = TimeSpan.FromMilliseconds(750);
    private readonly IAccountBrowserSessionManager _browserSessions;
    private readonly IAuthorizedAccountDataObservationPort _accountObservation;
    private readonly ILogger<LiveProviderCertificationCoordinator>? _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public LiveProviderCertificationCoordinator(
        IAccountBrowserSessionManager browserSessions,
        IAuthorizedAccountDataObservationPort accountObservation,
        ILogger<LiveProviderCertificationCoordinator>? logger = null)
    {
        _browserSessions = browserSessions;
        _accountObservation = accountObservation;
        _logger = logger;
    }

    public async Task<LiveProviderCertificationResult> CertifyAsync(
        LiveProviderCertificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid();
        var validation = Validate(request, DateTimeOffset.UtcNow);
        if (validation is not null)
        {
            return Blocked(request, correlationId, validation.Value.Code, validation.Value.Message);
        }

        var gate = _gates.GetOrAdd(request.AccountId, static _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(request, correlationId);
        }

        try
        {
            var runtime = await _browserSessions.GetStatusAsync(request.AccountId, cancellationToken);
            if (runtime.ProviderId != request.ProviderId)
            {
                return Blocked(request, correlationId, "PROVIDER_SCOPE_MISMATCH", "Authorization không khớp provider của browser context.", runtime);
            }

            if (runtime.RuntimeState != AccountBrowserRuntimeState.Open)
            {
                return Blocked(request, correlationId, "BROWSER_NOT_OPEN", "Chưa có phiên trình duyệt đang mở để kiểm tra dữ liệu tài khoản. Hãy mở phiên đã được quản lý hoặc yêu cầu đăng nhập lại; hệ thống không tự gửi credential/TOTP.", runtime);
            }

            if (runtime.BrowserProfileBindingId is null)
            {
                return Blocked(request, correlationId, "BROWSER_PROFILE_BINDING_MISSING", "Không xác định được profile binding của account đã chọn.", runtime);
            }

            var isActiveCandidate = runtime.ActiveAttemptId is not null;
            if (isActiveCandidate
                && request.Authorization!.AuthorizedAttemptId != runtime.ActiveAttemptId)
            {
                return Blocked(request, correlationId, "ATTEMPT_SCOPE_MISMATCH", "Authorization không khớp attempt browser đang sở hữu context hiện tại.", runtime);
            }

            if (runtime.AuthenticationState == AuthenticationObservationStatus.PostLoginPending)
            {
                if (!request.Authorization!.AllowPendingSessionObservation
                    || request.Authorization.AuthorizedAttemptId != runtime.ActiveAttemptId)
                {
                    return Blocked(request, correlationId, "PENDING_SESSION_AUTHORIZATION_REQUIRED", "Phiên candidate đang chờ provider hiển thị bằng chứng sau TOTP; cần authorization read-only gắn đúng attempt để quan sát tiếp.", runtime);
                }
            }
            else if (runtime.AuthenticationState != AuthenticationObservationStatus.AuthenticatedSessionConfirmed)
            {
                return Blocked(request, correlationId, "SESSION_NOT_CONFIRMED", "Chưa có bằng chứng phiên authenticated hiện tại; không tự đăng nhập hoặc gửi credential.", runtime);
            }

            var observationTimeout = NormalizeTimeout(request.ObservationTimeout);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(observationTimeout);
            AccountObservationResult observation;
            try
            {
                var persistenceMode = request.Mode == LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi
                    ? request.Authorization!.PersistNonSecretObservations
                        ? AccountObservationPersistenceMode.PersistChatGptUsageSnapshotOnly
                        : AccountObservationPersistenceMode.DoNotPersist
                    : request.Authorization!.PersistNonSecretObservations
                        ? AccountObservationPersistenceMode.PersistNonSecretObservations
                        : AccountObservationPersistenceMode.DoNotPersist;
                var retryDeadline = DateTimeOffset.UtcNow.Add(
                    observationTimeout < TransientObservationRetryWindow
                        ? observationTimeout
                        : TransientObservationRetryWindow);
                while (true)
                {
                    observation = await _accountObservation.ObserveAccountDataAsync(
                        request.AccountId,
                        persistenceMode,
                        timeout.Token,
                        request.Mode == LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi);

                    var scopeError = ValidateObservationScope(request, runtime, observation);
                    if (scopeError is not null || !IsTransientObservation(observation) || DateTimeOffset.UtcNow >= retryDeadline)
                    {
                        break;
                    }

                    _logger?.LogInformation(
                        "Retrying transient account observation for account {AccountId}; status {Status}.",
                        request.AccountId,
                        observation.Status);
                    await Task.Delay(TransientObservationRetryDelay, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(request, correlationId, runtime);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Blocked(request, correlationId, "CERTIFICATION_TIMEOUT", "Quan sát account vượt quá thời gian giới hạn; không tiếp tục retry vô hạn.", runtime);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(
                    "Account observation failed for account {AccountId}, attempt {AttemptId}; exception type {ExceptionType}.",
                    request.AccountId,
                    runtime.ActiveAttemptId,
                    exception.GetType().Name);
                return Blocked(
                    request,
                    correlationId,
                    "ACCOUNT_OBSERVATION_FAILED",
                    "Phiên vẫn được giữ, nhưng chưa đọc được dữ liệu tài khoản. Kiểm tra nhật ký của tài khoản để xem nguyên nhân.",
                    runtime);
            }

            var observationScopeError = ValidateObservationScope(request, runtime, observation);
            if (observationScopeError is not null)
            {
                return Blocked(
                    request,
                    correlationId,
                    observationScopeError.Value.Code,
                    observationScopeError.Value.Message,
                    runtime);
            }

            var status = observation.IsSuccess
                ? LiveProviderCertificationStatus.Completed
                : LiveProviderCertificationStatus.Inconclusive;
            var code = observation.Status;
            var message = observation.Message;
            return new LiveProviderCertificationResult(
                status,
                code,
                message,
                correlationId,
                request.AccountId,
                request.ProviderId,
                observation.Snapshot,
                observation.Observation,
                observation.IdentityStatus,
                observation.LocalObservationPersisted,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            gate.Release();
        }
    }

    private static (string Code, string Message)? Validate(
        LiveProviderCertificationRequest request,
        DateTimeOffset nowUtc)
    {
        if (request.AccountId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return ("CERTIFICATION_SCOPE_INVALID", "Certification cần account và provider cụ thể.");
        }

        if (!request.ProviderId.Equals(ChatGptProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return ("PROVIDER_NOT_SUPPORTED", "Live certification hiện chỉ hỗ trợ provider ChatGPT.");
        }

        var authorization = request.Authorization;
        if (authorization is null)
        {
            return ("LIVE_PROVIDER_AUTHORIZATION_REQUIRED", "Chưa có authorization rõ ràng cho provider/account đã chọn.");
        }

        if (authorization.AccountId != request.AccountId
            || !authorization.ProviderId.Equals(request.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return ("AUTHORIZATION_SCOPE_MISMATCH", "Authorization không khớp account hoặc provider được yêu cầu.");
        }

        if (!authorization.IsActive(nowUtc))
        {
            return ("AUTHORIZATION_EXPIRED", "Authorization đã hết hạn hoặc không hợp lệ.");
        }

        var isAllowedMode = request.Mode switch
        {
            LiveProviderCertificationMode.ExistingAuthenticatedSessionOnly => authorization.IsReadOnlyExistingSessionScope,
            LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi => authorization.IsReadOnlyChatGptUsageApiScope,
            _ => false
        };
        if (!isAllowedMode)
        {
            return ("LIVE_SCOPE_NOT_READ_ONLY", "Scope không khớp quyền read-only: không gửi credential/TOTP, không chụp ảnh, và chỉ cho phép GET usage ChatGPT khi người dùng bật đúng quyền API.");
        }

        if (string.IsNullOrWhiteSpace(authorization.ScopeReference))
        {
            return ("AUTHORIZATION_REFERENCE_MISSING", "Authorization phải có scope reference không rỗng.");
        }

        return null;
    }

    private static (string Code, string Message)? ValidateObservationScope(
        LiveProviderCertificationRequest request,
        BrowserRuntimeSnapshot runtime,
        AccountObservationResult observation)
    {
        if (observation.Snapshot.AccountId != request.AccountId
            || !observation.Snapshot.ProviderId.Equals(request.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return (
                "OBSERVATION_ACCOUNT_SCOPE_MISMATCH",
                "Observation không khớp account hoặc provider được certification; đã chặn kết quả.");
        }

        if (runtime.BrowserProfileBindingId is not Guid runtimeBindingId
            || observation.Snapshot.BrowserProfileBindingId != runtimeBindingId)
        {
            return (
                "OBSERVATION_PROFILE_BINDING_MISMATCH",
                "Observation không khớp browser profile binding đang sở hữu; không ghi nhận certification.");
        }

        if (observation.Observation is null)
        {
            return observation.IsSuccess
                ? (
                    "OBSERVATION_MISSING",
                    "Certification không có provider observation đi kèm; không thể kết luận hoàn tất.")
                : null;
        }

        var providerObservation = observation.Observation;
        if (providerObservation.AccountId != request.AccountId
            || !providerObservation.ProviderId.Equals(request.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return (
                "OBSERVATION_ACCOUNT_SCOPE_MISMATCH",
                "Provider observation không khớp account hoặc provider được chọn; đã chặn kết quả.");
        }

        if (providerObservation.BrowserProfileBindingId != runtimeBindingId)
        {
            return (
                "OBSERVATION_PROFILE_BINDING_MISMATCH",
                "Provider observation không khớp profile binding hiện tại; đã chặn kết quả.");
        }

        if (runtime.ActiveAttemptId is Guid activeAttemptId
            && providerObservation.AttemptId != activeAttemptId)
        {
            return (
                "OBSERVATION_ATTEMPT_SCOPE_MISMATCH",
                "Provider observation thuộc attempt khác; không sử dụng dữ liệu để certification.");
        }

        if (string.IsNullOrWhiteSpace(providerObservation.BrowserContextId))
        {
            return (
                "OBSERVATION_CONTEXT_MISSING",
                "Provider observation thiếu context identifier an toàn; không thể xác nhận ownership.");
        }

        return null;
    }

    private static TimeSpan NormalizeTimeout(TimeSpan? timeout)
        => timeout is { } value && value > TimeSpan.Zero && value <= TimeSpan.FromMinutes(2)
            ? value
            : DefaultObservationTimeout;

    private static bool IsTransientObservation(AccountObservationResult observation)
        => !observation.IsSuccess
            && observation.Status is "IDENTITY_EMAIL_UNAVAILABLE"
                or "IDENTITY_EVIDENCE_INSUFFICIENT"
                or "IDENTITY_UNCONFIRMED"
                or "IDENTITY_EMAIL_NOT_EXPOSED";

    private static LiveProviderCertificationResult Blocked(
        LiveProviderCertificationRequest request,
        Guid correlationId,
        string code,
        string message,
        BrowserRuntimeSnapshot? snapshot = null)
        => new(
            LiveProviderCertificationStatus.Blocked,
            code,
            message,
            correlationId,
            request.AccountId,
            request.ProviderId,
            snapshot ?? new BrowserRuntimeSnapshot(
                request.AccountId,
                null,
                request.ProviderId,
                AccountBrowserRuntimeState.Unknown,
                AuthenticationObservationStatus.NotChecked,
                IdentityVerificationStatus.NotChecked,
                DateTimeOffset.UtcNow,
                EvidenceSource: "Live certification authorization gate",
                SanitizedFailureCode: code),
            CompletedAtUtc: DateTimeOffset.UtcNow);

    private static LiveProviderCertificationResult Cancelled(
        LiveProviderCertificationRequest request,
        Guid correlationId,
        BrowserRuntimeSnapshot? snapshot = null)
        => new(
            LiveProviderCertificationStatus.Cancelled,
            "CERTIFICATION_CANCELLED",
            "Đã hủy live certification; không tiếp tục đọc hoặc ghi observation.",
            correlationId,
            request.AccountId,
            request.ProviderId,
            snapshot ?? new BrowserRuntimeSnapshot(
                request.AccountId,
                null,
                request.ProviderId,
                AccountBrowserRuntimeState.Unknown,
                AuthenticationObservationStatus.NotChecked,
                IdentityVerificationStatus.NotChecked,
                DateTimeOffset.UtcNow,
                EvidenceSource: "Live certification cancellation",
                SanitizedFailureCode: "CERTIFICATION_CANCELLED"),
            CompletedAtUtc: DateTimeOffset.UtcNow);

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }
    }
}
