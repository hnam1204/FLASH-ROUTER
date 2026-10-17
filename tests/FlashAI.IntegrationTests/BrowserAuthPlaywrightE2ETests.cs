using System.Collections.Concurrent;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit.Sdk;

namespace FlashAI.IntegrationTests;

[CollectionDefinition("BrowserAuthPlaywright", DisableParallelization = true)]
public sealed class BrowserAuthPlaywrightCollectionDefinition
{
}

[Collection("BrowserAuthPlaywright")]
public sealed class BrowserAuthPlaywrightE2ETests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();
    private readonly string _vaultRoot = Path.Combine(Path.GetTempPath(), "flash-router-playwright-vault-" + Guid.NewGuid().ToString("N"));
    private IDataProtectionProvider _dataProtection = null!;
    private ISecretVault _vault = null!;
    private VaultAccessState _vaultAccess = null!;
    private MockAuthenticationServer _server = null!;
    private string? _chromeExecutable;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _dataProtection = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_vaultRoot, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER AUTH BROWSER E2E"));
        _vault = new FileSecretVault(Path.Combine(_vaultRoot, "vault"), _dataProtection);
        _vaultAccess = new VaultAccessState();
        _vaultAccess.SetUnlocked(true);
        var runtime = new PlaywrightBrowserRuntime();
        var runtimeStatus = await runtime.GetStatusAsync();
        _chromeExecutable = runtimeStatus.IsReady ? runtimeStatus.ExecutablePath : null;
        _server = await MockAuthenticationServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        (_vault as IDisposable)?.Dispose();
        (_dataProtection as IDisposable)?.Dispose();
        await _database.DisposeAsync();
        DeleteTestDirectory(_vaultRoot);
    }

    [Fact]
    public async Task HeadedPlaywrightCompletesSyntheticLoginAndPersistsAccountOwnedSession()
    {
        RequireChrome();
        var accountA = await CreateAccountAsync("headed-a@example.test", "SyntheticPassword-A1");
        var accountB = await CreateAccountAsync("headed-b@example.test", "SyntheticPassword-B1");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();

        var sessionFactory = CreateSessionFactory(originPolicy, _server.LoginUri);
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);
        var started = await orchestrator.StartAsync(accountA);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        var email = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, email.State);

        var password = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, confirmed: true);
        Assert.Equal(BrowserAuthState.MfaRequired, password.State);

        var observed = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, confirmed: true);
        Assert.Equal(BrowserAuthState.BrowserSessionObserved, observed.State);
        Assert.Equal("BROWSER_SESSION_OBSERVED", observed.Status);
        Assert.NotNull(observed.Observation);

        var closed = await orchestrator.CancelAsync(started.AttemptId.Value);
        Assert.Equal(BrowserAuthState.Cancelled, closed.State);

        var preparedA = await profileManager.PrepareAsync(accountA);
        Assert.True(preparedA.IsSuccess, preparedA.Message);
        await using (var reopenedA = await CreateSessionFactory(originPolicy).OpenAsync(preparedA.Context!, BrowserOriginPolicy.ChatGptUri))
        {
            var snapshot = await reopenedA.ReadPageAsync();
            Assert.Equal(BrowserPageState.Authenticated, snapshot.State);
        }

        var preparedB = await profileManager.PrepareAsync(accountB);
        Assert.True(preparedB.IsSuccess, preparedB.Message);
        await using (var openedB = await CreateSessionFactory(originPolicy).OpenAsync(preparedB.Context!, BrowserOriginPolicy.ChatGptUri))
        {
            var snapshot = await openedB.ReadPageAsync();
            Assert.Equal(BrowserPageState.EmailRequired, snapshot.State);
            Assert.NotEqual(BrowserPageState.Authenticated, snapshot.State);
        }
    }

    [Fact]
    public async Task HeadedPlaywrightRecognizesGuestLandingAndActivatesLoginEntry()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("guest-landing@example.test", "SyntheticPassword-Guest");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, _server.GuestUri);
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.GuestLandingLoginAvailable, started.State);
        Assert.Equal("GUEST_LANDING_LOGIN_AVAILABLE", started.Status);
        Assert.Equal(BrowserAuthAction.ActivateLoginEntry, started.NextAction);
        Assert.Equal(_server.GuestUri, sessionFactory.LastOpenedUri);

        var confirmation = await orchestrator.ActivateLoginEntryAsync(started.AttemptId!.Value, confirmed: false);
        Assert.Equal("CONFIRMATION_REQUIRED", confirmation.Status);

        var email = await orchestrator.ActivateLoginEntryAsync(started.AttemptId.Value, confirmed: true);
        Assert.Equal(BrowserAuthState.EmailRequired, email.State);

        var filledEmail = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, filledEmail.State);

        var filledPassword = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, confirmed: true);
        Assert.Equal(BrowserAuthState.MfaRequired, filledPassword.State);

        var observed = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, confirmed: true);
        Assert.Equal(BrowserAuthState.BrowserSessionObserved, observed.State);
        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightAutoLoginRunsSingleConsentWorkflowAndPersistsSession()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("headed-auto@example.test", "SyntheticPassword-Auto");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, _server.GuestUri);
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var stages = new ConcurrentQueue<string>();
        var result = await orchestrator.AutoLoginAsync(
            accountId,
            credentialRotationAttested: true,
            progress: new Progress<BrowserAuthProgress>(item => stages.Enqueue(item.Stage)));

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Equal("BROWSER_SESSION_OBSERVED", result.Status);
        Assert.Contains("OpeningLogin", stages);
        Assert.Contains("FillingEmail", stages);
        Assert.Contains("FillingPassword", stages);
        Assert.Contains("FillingTotp", stages);

        await orchestrator.CancelAsync(result.AttemptId!.Value);
        var prepared = await profileManager.PrepareAsync(accountId);
        Assert.True(prepared.IsSuccess, prepared.Message);
        await using var reopened = await CreateSessionFactory(originPolicy, _server.GuestUri)
            .OpenAsync(prepared.Context!, BrowserOriginPolicy.ChatGptUri);
        var snapshot = await reopened.ReadPageAsync();
        Assert.Equal(BrowserPageState.Authenticated, snapshot.State);
    }

    [Fact]
    public async Task HeadedPlaywrightWaitsForTransientUnknownAcrossAllCredentialSteps()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync(
            "delayed-transitions@example.test", "SyntheticPassword-Delayed");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var delayedLoginUri = new Uri(_server.LoginUri, "?variant=delayed-transitions");
        var sessionFactory = CreateSessionFactory(originPolicy, delayedLoginUri);
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        // The fixture shows a DOM with no login fields for 700 ms after each
        // submit (email, password, TOTP). The observer must not stop on the
        // first Unknown snapshot and must still verify the final session.
        var result = await orchestrator.AutoLoginAsync(
            accountId, credentialRotationAttested: true);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Equal("BROWSER_SESSION_OBSERVED", result.Status);
        Assert.NotNull(result.Observation);

        await orchestrator.CancelAsync(result.AttemptId!.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightDoesNotTreatGuestNewChatTextAsAuthenticated()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("guest-new-chat@example.test", "SyntheticPassword-GuestNewChat");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, new Uri(_server.GuestUri, "?variant=guest-new-chat"));
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var started = await orchestrator.StartAsync(accountId);

        Assert.Equal(BrowserAuthState.GuestLandingLoginAvailable, started.State);
        Assert.NotEqual(BrowserAuthState.BrowserSessionObserved, started.State);

        await orchestrator.CancelAsync(started.AttemptId!.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightSupportsLocalizedSamePageEmailModalsAndProviderAlternatives()
    {
        RequireChrome();
        foreach (var variant in new[] { "modal-en", "modal-vi" })
        {
            var accountId = await CreateAccountAsync($"{variant}@example.test", "SyntheticPassword-Modal");
            using var profileManager = CreateProfileManager();
            var originPolicy = CreateLocalOriginPolicy();
            var sessionFactory = CreateSessionFactory(originPolicy, new Uri(_server.GuestUri, $"?variant={variant}"));
            await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

            var started = await orchestrator.StartAsync(accountId);
            Assert.Equal(BrowserAuthState.GuestLandingLoginAvailable, started.State);

            var emailStep = await orchestrator.ActivateLoginEntryAsync(started.AttemptId!.Value, confirmed: true);
            Assert.Equal(BrowserAuthState.EmailRequired, emailStep.State);
            Assert.Equal(sessionFactory.LastOpenedUri?.Host, sessionFactory.ActiveCurrentUri?.Host);
            Assert.Equal(sessionFactory.LastOpenedUri?.AbsolutePath, sessionFactory.ActiveCurrentUri?.AbsolutePath);

            var passwordStep = await orchestrator.ConfirmAndFillAsync(
                started.AttemptId.Value,
                BrowserAuthAction.FillEmail,
                confirmed: true);
            Assert.Equal(BrowserAuthState.PasswordRequired, passwordStep.State);

            await orchestrator.CancelAsync(started.AttemptId.Value);
        }
    }

    [Fact]
    public async Task HeadedPlaywrightFailsClosedForAmbiguousEmailContinueActions()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("ambiguous-email@example.test", "SyntheticPassword-AmbiguousEmail");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, new Uri(_server.GuestUri, "?variant=modal-ambiguous"));
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var started = await orchestrator.StartAsync(accountId);
        var emailStep = await orchestrator.ActivateLoginEntryAsync(started.AttemptId!.Value, confirmed: true);
        Assert.Equal(BrowserAuthState.EmailRequired, emailStep.State);

        var result = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillEmail,
            confirmed: true);
        Assert.Equal(BrowserAuthState.Failed, result.State);
        Assert.Equal("EMAIL_CONTINUE_AMBIGUOUS", result.Status);

        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightFailsClosedWhenEmailContinueIsMissing()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("missing-email-continue@example.test", "SyntheticPassword-MissingContinue");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, new Uri(_server.GuestUri, "?variant=modal-no-continue"));
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var started = await orchestrator.StartAsync(accountId);
        var emailStep = await orchestrator.ActivateLoginEntryAsync(started.AttemptId!.Value, confirmed: true);
        Assert.Equal(BrowserAuthState.EmailRequired, emailStep.State);

        var result = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillEmail,
            confirmed: true);
        Assert.Equal(BrowserAuthState.Failed, result.State);
        Assert.Equal("EMAIL_CONTINUE_NOT_FOUND", result.Status);

        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightSupportsLocalizedLinkAndPopupLoginEntries()
    {
        RequireChrome();
        foreach (var variant in new[] { "english-link", "popup" })
        {
            var accountId = await CreateAccountAsync($"{variant}@example.test", "SyntheticPassword-Localized");
            using var profileManager = CreateProfileManager();
            var originPolicy = CreateLocalOriginPolicy();
            var sessionFactory = CreateSessionFactory(originPolicy, new Uri(_server.GuestUri, $"?variant={variant}"));
            await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

            var started = await orchestrator.StartAsync(accountId);
            Assert.Equal(BrowserAuthState.GuestLandingLoginAvailable, started.State);

            var email = await orchestrator.ActivateLoginEntryAsync(started.AttemptId!.Value, confirmed: true);
            Assert.Equal(BrowserAuthState.EmailRequired, email.State);
            await orchestrator.CancelAsync(started.AttemptId.Value);
        }
    }

    [Fact]
    public async Task HeadedPlaywrightRejectsAmbiguousGuestLoginEntry()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("ambiguous-guest@example.test", "SyntheticPassword-Ambiguous");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, new Uri(_server.GuestUri, "?variant=ambiguous"));
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var result = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.Unknown, result.State);
        Assert.Equal("UNKNOWN_LOGIN_STATE", result.Status);
        await orchestrator.CancelAsync(result.AttemptId!.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightKeepsGuestSessionAliveForSixtySeconds()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("stable-guest@example.test", "SyntheticPassword-Stable");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy, _server.GuestUri);
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.GuestLandingLoginAvailable, started.State);

        await Task.Delay(TimeSpan.FromSeconds(60));
        var refreshed = await orchestrator.RefreshAsync(started.AttemptId!.Value);
        Assert.Equal(BrowserAuthState.GuestLandingLoginAvailable, refreshed.State);
        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task HeadedPlaywrightStopsAtControlledHumanActionStates()
    {
        RequireChrome();
        foreach (var scenario in new[]
        {
            (Variant: "captcha", State: BrowserAuthState.HumanActionRequired, Status: "CAPTCHA_REQUIRED"),
            (Variant: "sso", State: BrowserAuthState.ExternalProviderRequired, Status: "EXTERNAL_PROVIDER_REQUIRED"),
            (Variant: "rate-limited", State: BrowserAuthState.HumanActionRequired, Status: "RATE_LIMITED"),
            (Variant: "unknown", State: BrowserAuthState.Unknown, Status: "UNKNOWN_LOGIN_STATE")
        })
        {
            var accountId = await CreateAccountAsync($"{scenario.Variant}@example.test", "SyntheticPassword-123");
            using var profileManager = CreateProfileManager();
            var originPolicy = CreateLocalOriginPolicy();
            var loginUri = new Uri(_server.LoginUri, $"?variant={scenario.Variant}");
            var sessionFactory = CreateSessionFactory(originPolicy, loginUri);
            await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

            var result = await orchestrator.StartAsync(accountId);

            Assert.Equal(scenario.State, result.State);
            Assert.Equal(scenario.Status, result.Status);
            Assert.Null(result.NextAction);
            if (result.AttemptId is Guid attemptId)
            {
                await orchestrator.CancelAsync(attemptId);
            }
        }
    }

    [Fact]
    public async Task HeadedPlaywrightStopsOnInvalidCredentialTransitions()
    {
        RequireChrome();
        foreach (var scenario in new[]
        {
            (Variant: "invalid-password", ExpectedState: BrowserAuthState.Unknown, ExpectedStatus: "UNKNOWN_LOGIN_STATE"),
            (Variant: "invalid-totp", ExpectedState: BrowserAuthState.MfaRequired, ExpectedStatus: "MFA_REQUIRED")
        })
        {
            var accountId = await CreateAccountAsync($"{scenario.Variant}@example.test", "SyntheticPassword-456");
            using var profileManager = CreateProfileManager();
            var originPolicy = CreateLocalOriginPolicy();
            var sessionFactory = CreateSessionFactory(
                originPolicy,
                new Uri(_server.LoginUri, $"?variant={scenario.Variant}"));
            await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

            var started = await orchestrator.StartAsync(accountId);
            var email = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
            Assert.Equal(BrowserAuthState.PasswordRequired, email.State);

            var password = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, confirmed: true);
            if (scenario.Variant == "invalid-password")
            {
                Assert.Equal(scenario.ExpectedState, password.State);
                Assert.Equal(scenario.ExpectedStatus, password.Status);
            }
            else
            {
                Assert.Equal(BrowserAuthState.MfaRequired, password.State);
                var totp = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, confirmed: true);
                Assert.Equal(scenario.ExpectedState, totp.State);
                Assert.Equal(scenario.ExpectedStatus, totp.Status);
            }

            await orchestrator.CancelAsync(started.AttemptId.Value);
        }
    }

    [Fact]
    public async Task HeadedPlaywrightReportsExternalBrowserClosure()
    {
        RequireChrome();
        var accountId = await CreateAccountAsync("closed-browser@example.test", "SyntheticPassword-789");
        using var profileManager = CreateProfileManager();
        var originPolicy = CreateLocalOriginPolicy();
        var sessionFactory = CreateSessionFactory(originPolicy);
        await using var orchestrator = CreateOrchestrator(profileManager, sessionFactory, originPolicy);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        await sessionFactory.CloseActiveAsync();
        var closed = await orchestrator.RefreshAsync(started.AttemptId!.Value);

        Assert.False(closed.IsSuccess);
        Assert.Equal(BrowserAuthState.Failed, closed.State);
        Assert.Equal("BROWSER_CLOSED", closed.Status);
    }

    private BrowserProfileManager CreateProfileManager()
        => new(
            _database.ContextFactory,
            new LocalDataPaths(_database.Root),
            new FixedChromeResolver(_chromeExecutable!));

    private BrowserAuthOrchestrator CreateOrchestrator(
        BrowserProfileManager profileManager,
        LocalPlaywrightSessionFactory sessionFactory,
        Func<Uri?, bool> originPolicy)
        => new(
            profileManager,
            sessionFactory,
            new StableBrowserPageStateDetector(originPolicy),
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            new Rfc6238TotpGenerator(),
            originPolicy);

    private LocalPlaywrightSessionFactory CreateSessionFactory(
        Func<Uri?, bool> originPolicy,
        Uri? loginUri = null)
        => new(loginUri ?? _server.LoginUri, originPolicy);

    private Func<Uri?, bool> CreateLocalOriginPolicy()
        => uri => uri is not null
            && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && uri.Port == _server.Port;

    private async Task<Guid> CreateAccountAsync(string email, string password)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = email.Split('@')[0],
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        var accountId = (await reader.GetAccountsAsync(email)).Single().AccountId;
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var saved = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, password, "JBSWY3DPEHPK3PXP", false));
        Assert.True(saved.IsSuccess, saved.Message);
        return accountId;
    }

    private void RequireChrome()
    {
        if (string.IsNullOrWhiteSpace(_chromeExecutable))
        {
        throw SkipException.ForSkip("Playwright-managed Chromium is not installed for Microsoft.Playwright 1.63.0.");
        }
    }

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FixedChromeResolver(string path) : IChromeExecutableResolver
    {
        public string? Resolve() => path;
    }
}

internal sealed class LocalPlaywrightSessionFactory : IBrowserAuthenticationSessionFactory
{
    private readonly Uri _loginUri;
    private readonly Func<Uri?, bool> _originPolicy;
    private IBrowserContext? _activeContext;

    public Uri? LastOpenedUri { get; private set; }
    public Uri? ActiveCurrentUri => _activeSession?.CurrentUri;
    private IBrowserAuthenticationSession? _activeSession;

    public LocalPlaywrightSessionFactory(Uri loginUri, Func<Uri?, bool> originPolicy)
    {
        _loginUri = loginUri;
        _originPolicy = originPolicy;
    }

    public async Task<IBrowserAuthenticationSession> OpenAsync(
        BrowserProfileSessionContext profile,
        Uri officialUri,
        CancellationToken cancellationToken = default)
    {
        if (!BrowserOriginPolicy.IsAllowed(officialUri))
        {
            throw new BrowserAuthSessionException("BROWSER_ORIGIN_NOT_ALLOWED", "Synthetic factory received an unexpected production destination.");
        }

        IPlaywright? playwright = null;
        IBrowserContext? context = null;
        try
        {
            playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
            context = await playwright.Chromium.LaunchPersistentContextAsync(
                profile.ProfileDirectory,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    ChromiumSandbox = true,
                    Args = ["--new-window", "--no-first-run", "--disable-background-networking", "--disable-default-apps", "--disable-sync"],
                    Timeout = 30_000
                }).WaitAsync(cancellationToken);

            var page = context.Pages.Count > 0
                ? context.Pages[0]
                : await context.NewPageAsync().WaitAsync(cancellationToken);
            var session = new PlaywrightBrowserAuthenticationSession(playwright, context, page, _originPolicy);
            _activeContext = context;
            _activeSession = session;
            playwright = null;
            context = null;
            LastOpenedUri = _loginUri;
            await session.NavigateAsync(_loginUri, cancellationToken);
            return session;
        }
        catch
        {
            if (context is not null)
            {
                await context.CloseAsync();
            }

            playwright?.Dispose();
            throw;
        }
    }

    public Task CloseActiveAsync()
        => _activeContext?.CloseAsync() ?? Task.CompletedTask;
}

internal sealed class MockAuthenticationServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private MockAuthenticationServer(WebApplication app, Uri baseUri)
    {
        _app = app;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }
    public int Port => BaseUri.Port;
    public Uri GuestUri => new(BaseUri, "/");
    public Uri LoginUri => new(BaseUri, "/login");

    public static async Task<MockAuthenticationServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MockAuthenticationServer).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/", (HttpRequest request) =>
        {
            var variant = request.Query["variant"].ToString();
            var session = request.Cookies["mock_session"];
            return Results.Content(RenderGuest(variant, session), "text/html; charset=utf-8");
        });
        app.MapGet("/login", (HttpRequest request) =>
        {
            var variant = request.Query["variant"].ToString();
            var session = request.Cookies["mock_session"];
            return Results.Content(Render(variant, session), "text/html; charset=utf-8");
        });
        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? app.Urls;
        return new MockAuthenticationServer(app, new Uri(addresses.Single(), UriKind.Absolute));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Render(string variant, string? session)
    {
        if (!string.IsNullOrWhiteSpace(session))
        {
            return HomeHtml(session);
        }

        return variant switch
        {
            "captcha" => "<main><h1>CAPTCHA challenge</h1><p>Verify you are human.</p></main>",
            "sso" => "<main><h1>Single sign-on required</h1><p>Continue with Microsoft.</p></main>",
            "rate-limited" => "<main><h1>Too many requests</h1><p>Please try again later.</p></main>",
            "unknown" => "<main><h1>Unknown challenge</h1><p>Unsupported login state.</p></main>",
            _ => LoginHtml(variant)
        };
    }

    private static string RenderGuest(string variant, string? session)
    {
        if (!string.IsNullOrWhiteSpace(session))
        {
            return HomeHtml(session);
        }

        return variant switch
        {
            "english-link" => "<main><h1>ChatGPT</h1><p>Public landing</p><a href=\"/login\">Log in</a><a href=\"/register\">Sign up</a></main>",
            "popup" => "<main><h1>ChatGPT</h1><p>Public landing</p><button id=\"login-entry\" type=\"button\" onclick=\"window.open('/login', '_blank')\">Đăng nhập</button></main>",
            "ambiguous" => "<main><h1>ChatGPT</h1><button>Đăng nhập</button><button>Log in</button></main>",
            "guest-new-chat" => "<main><h1>ChatGPT</h1><p>New chat</p><p>Public landing</p><button id=\"login-entry\" type=\"button\" onclick=\"location.href='/login'\">Log in</button></main>",
            "missing" => "<main><h1>ChatGPT</h1><p>Public landing without an authentication entry.</p></main>",
            "modal-en" => ModalGuestHtml("Log in", "Continue", string.Empty),
            "modal-vi" => ModalGuestHtml("Đăng nhập", "Tiếp tục", string.Empty),
            "modal-ambiguous" => ModalGuestHtml("Log in", "Continue", "<button id='modal-continue-2' type='button'>Continue</button>"),
            "modal-no-continue" => ModalGuestHtml("Log in", string.Empty, string.Empty),
            _ => "<main><h1>ChatGPT</h1><p>Public landing</p><button id=\"login-entry\" type=\"button\" onclick=\"location.href='/login'\">Đăng nhập</button><a href=\"/register\">Đăng ký</a></main>"
        };
    }

    private static string HomeHtml(string session)
        => $"<main><h1>Welcome home</h1><p>New chat</p><p>Signed in as {System.Net.WebUtility.HtmlEncode(session)}</p><button>Sign out</button></main>";

    private static string LoginHtml(string variant)
        => """
<!doctype html>
<html><head><meta charset="utf-8"><title>Mock Login</title></head>
<body><main><h1>Sign in</h1><label>Email</label><input id="mobile-auth-email" type="email" autocomplete="email" aria-label="Email"><button id="continue" type="button">Continue</button></main>
<script>
const variant = "__VARIANT__";
let email = "";
function error(text) { document.body.innerHTML = "<main><h1>Login error</h1><p>" + text + "</p></main>"; }
function transition(nextStep) {
  document.body.innerHTML = "<main><h1>Loading authentication step</h1><p>Please wait</p></main>";
  setTimeout(nextStep, 700);
}
function home() {
  document.cookie = "mock_session=" + encodeURIComponent(email) + "; Max-Age=3600; Path=/; SameSite=Lax";
  document.body.innerHTML = "<main><h1>Welcome home</h1><p>New chat</p><p>Sign out</p></main>";
}
function mfa() {
  document.body.innerHTML = "<main><h1>Authenticator challenge</h1><input id=\"code\" name=\"code\" inputmode=\"numeric\" autocomplete=\"one-time-code\" aria-label=\"Authenticator code\"><button id=\"totp-continue\" type=\"button\">Continue</button><p id=\"error\"></p></main>";
  const input = document.querySelector("#code");
  document.querySelector("#totp-continue").addEventListener("click", () => {
    if (input.value.length === 6) {
      if (variant === "invalid-totp") {
        document.querySelector("#error").textContent = "Invalid authenticator code";
      } else {
        if (variant === "delayed-transitions") transition(home); else home();
      }
    }
  });
}
function password() {
  document.body.innerHTML = "<main><h1>Password required</h1><input id=\"password\" type=\"password\" autocomplete=\"current-password\" aria-label=\"Password\"><button id=\"password-continue\" type=\"button\">Continue</button></main>";
  const input = document.querySelector("#password");
  document.querySelector("#password-continue").addEventListener("click", () => {
    if (input.value.length > 0) {
      if (variant === "invalid-password") {
        error("Invalid password");
      } else {
        if (variant === "delayed-transitions") transition(mfa); else mfa();
      }
    }
  });
}
const emailInput = document.querySelector("#mobile-auth-email");
emailInput.addEventListener("input", () => {
  email = emailInput.value;
});
document.querySelector("#continue").addEventListener("click", () => {
  if (email.includes("@")) {
    if (variant === "invalid-email") {
      error("Invalid email");
    } else {
      if (variant === "delayed-transitions") transition(password); else password();
    }
  }
});
</script></body></html>
""".Replace("__VARIANT__", variant, StringComparison.Ordinal);

    private static string ModalGuestHtml(string loginText, string continueText, string extraContinueButton)
        => """
<!doctype html>
<html><head><meta charset="utf-8"><title>ChatGPT Guest</title></head>
<body><main><h1>ChatGPT</h1><p>Public landing</p><button id="login-entry" data-mobile-auth-entry-action="login" type="button">__LOGIN_TEXT__</button></main>
<script>
document.querySelector("#login-entry").addEventListener("click", () => {
  document.body.innerHTML = "<main role=\"dialog\" aria-modal=\"true\"><h1>Sign in</h1><button type=\"button\">Continue with Google</button><button type=\"button\">Continue with Apple</button><button type=\"button\">Continue with phone</button><form><label>Email</label><input id=\"modal-email\" type=\"email\" autocomplete=\"email\" aria-label=\"Email\"><button id=\"modal-continue\" type=\"button\">__CONTINUE_TEXT__</button>__EXTRA_CONTINUE__</form></main>";
  const emailInput = document.querySelector("#modal-email");
  const continueButton = document.querySelector("#modal-continue");
  if (continueButton) {
    continueButton.addEventListener("click", () => {
      if (emailInput.value.includes("@")) {
        document.body.innerHTML = "<main><h1>Password required</h1><input id=\"password\" type=\"password\" autocomplete=\"current-password\" aria-label=\"Password\"></main>";
      }
    });
  }
});
</script></body></html>
"""
            .Replace("__LOGIN_TEXT__", loginText, StringComparison.Ordinal)
            .Replace("__CONTINUE_TEXT__", continueText, StringComparison.Ordinal)
            .Replace("__EXTRA_CONTINUE__", extraContinueButton, StringComparison.Ordinal);
}

