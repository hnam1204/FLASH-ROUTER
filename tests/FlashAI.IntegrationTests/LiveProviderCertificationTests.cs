using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.IntegrationTests;

public sealed class LiveProviderCertificationTests
{
    [Fact]
    public async Task MissingAuthorizationIsBlockedBeforeBrowserObservation()
    {
        var accountId = Guid.NewGuid();
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(accountId));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", null));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("LIVE_PROVIDER_AUTHORIZATION_REQUIRED", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task CredentialSubmissionScopeIsRejectedFailClosed()
    {
        var accountId = Guid.NewGuid();
        var authorization = ValidAuthorization(accountId) with { SubmitTotp = true };
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(accountId));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", authorization));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("LIVE_SCOPE_NOT_READ_ONLY", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task UsageEndpointRequiresItsSpecificReadOnlyAuthorizationAndPassesExplicitScope()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var observation = new FakeAuthorizedObservationPort { Result = SuccessfulObservation(accountId, runtime) };
        using var coordinator = new LiveProviderCertificationCoordinator(new FakeBrowserSessionManager(runtime), observation);
        var authorization = LiveProviderAuthorization.ReadOnlyExistingSessionUsageApi(
            accountId,
            "openai-chatgpt",
            persistNonSecretObservations: false,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            TimeSpan.FromMinutes(2));

        var result = await coordinator.CertifyAsync(new LiveProviderCertificationRequest(
            accountId,
            "openai-chatgpt",
            authorization,
            LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi));

        Assert.Equal(LiveProviderCertificationStatus.Completed, result.Status);
        Assert.Equal(1, observation.CallCount);
        Assert.True(observation.LastReadChatGptUsageApi);
        Assert.Equal(AccountObservationPersistenceMode.DoNotPersist, observation.LastPersistenceMode);
    }

    [Fact]
    public async Task UsageEndpointCanPersistOnlyItsNormalizedSnapshotWhenRequested()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var observation = new FakeAuthorizedObservationPort
        {
            Result = SuccessfulObservation(accountId, runtime) with { LocalObservationPersisted = true }
        };
        using var coordinator = new LiveProviderCertificationCoordinator(new FakeBrowserSessionManager(runtime), observation);
        var authorization = LiveProviderAuthorization.ReadOnlyExistingSessionUsageApi(
            accountId,
            "openai-chatgpt",
            persistNonSecretObservations: true,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            TimeSpan.FromMinutes(2));

        var result = await coordinator.CertifyAsync(new LiveProviderCertificationRequest(
            accountId,
            "openai-chatgpt",
            authorization,
            LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi));

        Assert.Equal(LiveProviderCertificationStatus.Completed, result.Status);
        Assert.Equal(AccountObservationPersistenceMode.PersistChatGptUsageSnapshotOnly, observation.LastPersistenceMode);
        Assert.True(result.LocalObservationPersisted);
    }

    [Fact]
    public async Task TransientMissingIdentityEvidenceIsRetriedThenUsageCompletes()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var successful = SuccessfulObservation(accountId, runtime);
        var observation = new FakeAuthorizedObservationPort();
        observation.Results.Enqueue(successful with
        {
            IsSuccess = false,
            Status = "IDENTITY_EMAIL_UNAVAILABLE",
            Message = "Account UI is still loading identity evidence.",
            IdentityStatus = IdentityVerificationStatus.Unverified
        });
        observation.Results.Enqueue(successful with
        {
            Status = "CHATGPT_USAGE_API_OBSERVED"
        });
        using var coordinator = new LiveProviderCertificationCoordinator(new FakeBrowserSessionManager(runtime), observation);
        var authorization = LiveProviderAuthorization.ReadOnlyExistingSessionUsageApi(
            accountId,
            "openai-chatgpt",
            persistNonSecretObservations: true,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            TimeSpan.FromMinutes(2));

        var result = await coordinator.CertifyAsync(new LiveProviderCertificationRequest(
            accountId,
            "openai-chatgpt",
            authorization,
            LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(LiveProviderCertificationStatus.Completed, result.Status);
        Assert.Equal("CHATGPT_USAGE_API_OBSERVED", result.Code);
        Assert.Equal(2, observation.CallCount);
    }

    [Fact]
    public async Task UiOnlyAuthorizationCannotBeReusedForUsageEndpoint()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(new FakeBrowserSessionManager(runtime), observation);

        var result = await coordinator.CertifyAsync(new LiveProviderCertificationRequest(
            accountId,
            "openai-chatgpt",
            ValidAuthorization(accountId),
            LiveProviderCertificationMode.ExistingAuthenticatedSessionUsageApi));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("LIVE_SCOPE_NOT_READ_ONLY", result.Code);
        Assert.Equal(0, observation.CallCount);
        Assert.False(observation.LastReadChatGptUsageApi);
    }

    [Fact]
    public async Task AccountAndProviderScopeMustMatchAuthorization()
    {
        var accountId = Guid.NewGuid();
        var authorization = ValidAuthorization(Guid.NewGuid());
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(accountId));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", authorization));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("AUTHORIZATION_SCOPE_MISMATCH", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task UnsupportedProviderIsBlockedBeforeObservation()
    {
        var accountId = Guid.NewGuid();
        var authorization = ValidAuthorization(accountId) with { ProviderId = "openai-platform" };
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(accountId));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-platform", authorization));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("PROVIDER_NOT_SUPPORTED", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task CallerCancellationReturnsStructuredCancelledResult()
    {
        var accountId = Guid.NewGuid();
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(accountId));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", ValidAuthorization(accountId)),
            cancellation.Token);

        Assert.Equal(LiveProviderCertificationStatus.Cancelled, result.Status);
        Assert.Equal("CERTIFICATION_CANCELLED", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task UnconfirmedSessionCannotEnterLiveCertification()
    {
        var accountId = Guid.NewGuid();
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(accountId) with
        {
            AuthenticationState = AuthenticationObservationStatus.LoginRequired
        });
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", ValidAuthorization(accountId)));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("SESSION_NOT_CONFIRMED", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task MissingBrowserReturnsActionableBrowserNotOpenResult()
    {
        var accountId = Guid.NewGuid();
        var browser = new FakeBrowserSessionManager(new BrowserRuntimeSnapshot(
            accountId,
            Guid.NewGuid(),
            "openai-chatgpt",
            AccountBrowserRuntimeState.Closed,
            AuthenticationObservationStatus.NotChecked,
            IdentityVerificationStatus.NotChecked,
            DateTimeOffset.UtcNow));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", ValidAuthorization(accountId)));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("BROWSER_NOT_OPEN", result.Code);
        Assert.Contains("Chưa có phiên trình duyệt đang mở", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task MatchingActiveCandidateCanBeObservedOnlyWithPendingAttemptScope()
    {
        var accountId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(
            accountId,
            AuthenticationObservationStatus.PostLoginPending,
            attemptId));
        var observation = new FakeAuthorizedObservationPort
        {
            Result = SuccessfulObservation(accountId, browser.Status)
        };
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var authorization = ValidAuthorization(accountId) with
        {
            AuthorizedAttemptId = attemptId,
            AllowPendingSessionObservation = true
        };
        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", authorization));

        Assert.Equal(LiveProviderCertificationStatus.Completed, result.Status);
        Assert.Equal(1, observation.CallCount);
    }

    [Fact]
    public async Task PendingCandidateWithoutMatchingAuthorizationIsBlocked()
    {
        var accountId = Guid.NewGuid();
        var activeAttemptId = Guid.NewGuid();
        var browser = new FakeBrowserSessionManager(ConfirmedStatus(
            accountId,
            AuthenticationObservationStatus.PostLoginPending,
            activeAttemptId));
        var observation = new FakeAuthorizedObservationPort();
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", ValidAuthorization(accountId)));

        Assert.Equal(LiveProviderCertificationStatus.Blocked, result.Status);
        Assert.Equal("ATTEMPT_SCOPE_MISMATCH", result.Code);
        Assert.Equal(0, observation.CallCount);
    }

    [Fact]
    public async Task ExistingSessionCanBeObservedWithoutLocalPersistencePermission()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var browser = new FakeBrowserSessionManager(runtime);
        var observation = new FakeAuthorizedObservationPort
        {
            Result = SuccessfulObservation(accountId, runtime)
        };
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var authorization = ValidAuthorization(accountId) with { PersistNonSecretObservations = false };
        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", authorization));

        Assert.Equal(LiveProviderCertificationStatus.Completed, result.Status);
        Assert.Equal("ACCOUNT_DATA_OBSERVED", result.Code);
        Assert.False(result.LocalObservationPersisted);
        Assert.Equal(AccountObservationPersistenceMode.DoNotPersist, observation.LastPersistenceMode);
    }

    [Fact]
    public async Task AuthorizedLocalObservationUsesExistingPersistenceModel()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var browser = new FakeBrowserSessionManager(runtime);
        var observation = new FakeAuthorizedObservationPort
        {
            Result = SuccessfulObservation(accountId, runtime) with { LocalObservationPersisted = true }
        };
        using var coordinator = new LiveProviderCertificationCoordinator(browser, observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", ValidAuthorization(accountId)));

        Assert.Equal(LiveProviderCertificationStatus.Completed, result.Status);
        Assert.True(result.LocalObservationPersisted);
        Assert.Equal(AccountObservationPersistenceMode.PersistNonSecretObservations, observation.LastPersistenceMode);
    }

    [Fact]
    public async Task UnverifiedIdentityCannotBeReportedAsCompletedCertification()
    {
        var accountId = Guid.NewGuid();
        var runtime = ConfirmedStatus(accountId);
        var observation = new FakeAuthorizedObservationPort
        {
            Result = new AccountObservationResult(
                false,
                "IDENTITY_EMAIL_UNAVAILABLE",
                "Identity evidence is insufficient.",
                runtime,
                new ProviderAccountObservationSnapshot(
                    Guid.NewGuid(),
                    accountId,
                    Guid.NewGuid(),
                    runtime.BrowserProfileBindingId!.Value,
                    "openai-chatgpt",
                    "managed-context",
                    DateTimeOffset.UtcNow,
                    "en",
                    true,
                    true,
                    "PartiallyResolved",
                    "Visible provider account-menu UI",
                    "IDENTITY_EMAIL_NOT_EXPOSED",
                    []),
                IdentityVerificationStatus.Unverified)
        };
        using var coordinator = new LiveProviderCertificationCoordinator(
            new FakeBrowserSessionManager(runtime),
            observation);

        var result = await coordinator.CertifyAsync(
            new LiveProviderCertificationRequest(accountId, "openai-chatgpt", ValidAuthorization(accountId)));

        Assert.Equal(LiveProviderCertificationStatus.Inconclusive, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDENTITY_EMAIL_UNAVAILABLE", result.Code);
    }

    private static LiveProviderAuthorization ValidAuthorization(Guid accountId)
        => LiveProviderAuthorization.ReadOnlyExistingSession(
            accountId,
            "openai-chatgpt",
            persistNonSecretObservations: true,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            TimeSpan.FromMinutes(5));

    private static BrowserRuntimeSnapshot ConfirmedStatus(
        Guid accountId,
        AuthenticationObservationStatus authenticationState = AuthenticationObservationStatus.AuthenticatedSessionConfirmed,
        Guid? activeAttemptId = null)
        => new(
            accountId,
            Guid.NewGuid(),
            "openai-chatgpt",
            AccountBrowserRuntimeState.Open,
            authenticationState,
            IdentityVerificationStatus.Unverified,
            DateTimeOffset.UtcNow,
            EvidenceSource: "synthetic certification fixture",
            ActiveAttemptId: activeAttemptId);

    private static AccountObservationResult SuccessfulObservation(
        Guid accountId,
        BrowserRuntimeSnapshot? runtime = null)
    {
        runtime ??= ConfirmedStatus(accountId);
        var attemptId = runtime.ActiveAttemptId ?? Guid.NewGuid();
        return new(
            true,
            "ACCOUNT_DATA_OBSERVED",
            "Synthetic account observation completed.",
            runtime,
            new ProviderAccountObservationSnapshot(
                Guid.NewGuid(),
                accountId,
                attemptId,
                runtime.BrowserProfileBindingId!.Value,
                "openai-chatgpt",
                "synthetic-context",
                DateTimeOffset.UtcNow,
                "en",
                true,
                true,
                "Observed",
                "Synthetic official account UI",
                null,
                []),
            IdentityVerificationStatus.IdentityVerified);
    }

    private sealed class FakeBrowserSessionManager(BrowserRuntimeSnapshot status) : IAccountBrowserSessionManager
    {
        public BrowserRuntimeSnapshot Status => status;

        public Task<BrowserRuntimeSnapshot> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(status);

        public Task<BrowserOperationResult> OpenAsync(Guid accountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BrowserOperationResult> CloseAsync(Guid accountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BrowserOperationResult> ReopenAsync(Guid accountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionObservationResult> CheckSessionAsync(Guid accountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReconcileAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAuthorizedObservationPort : IAuthorizedAccountDataObservationPort
    {
        public Queue<AccountObservationResult> Results { get; } = new();
        public AccountObservationResult Result { get; set; } = new(
            false,
            "UNSET",
            "No synthetic result configured.",
            new BrowserRuntimeSnapshot(
                Guid.Empty,
                null,
                "openai-chatgpt",
                AccountBrowserRuntimeState.Unknown,
                AuthenticationObservationStatus.Unknown,
                IdentityVerificationStatus.Unknown,
                DateTimeOffset.UtcNow));

        public int CallCount { get; private set; }
        public AccountObservationPersistenceMode? LastPersistenceMode { get; private set; }
        public bool LastReadChatGptUsageApi { get; private set; }

        public Task<AccountObservationResult> ObserveAccountDataAsync(
            Guid accountId,
            AccountObservationPersistenceMode persistenceMode,
            CancellationToken cancellationToken = default,
            bool readChatGptUsageApi = false)
        {
            CallCount++;
            LastPersistenceMode = persistenceMode;
            LastReadChatGptUsageApi = readChatGptUsageApi;
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
        }
    }
}
