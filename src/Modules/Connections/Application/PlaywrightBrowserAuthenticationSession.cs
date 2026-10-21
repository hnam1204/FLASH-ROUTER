using System.Text.RegularExpressions;
using System.Text.Json;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Playwright;

namespace FlashAI.Modules.Connections.Application;

public sealed class PlaywrightBrowserAuthenticationSessionFactory : IBrowserAuthenticationSessionFactory
{
    private readonly IBrowserProfileOwnershipCoordinator _ownership;
    private readonly IChromeProfileLockInspector _lockInspector;
    private readonly IPlaywrightBrowserRuntime _browserRuntime;

    public PlaywrightBrowserAuthenticationSessionFactory(
        IBrowserProfileOwnershipCoordinator? ownership = null,
        IChromeProfileLockInspector? lockInspector = null,
        IPlaywrightBrowserRuntime? browserRuntime = null)
    {
        _ownership = ownership ?? new BrowserProfileOwnershipCoordinator();
        _lockInspector = lockInspector ?? new ChromeProfileLockInspector();
        _browserRuntime = browserRuntime ?? new PlaywrightBrowserRuntime();
    }

    public async Task<IBrowserAuthenticationSession> OpenAsync(
        BrowserProfileSessionContext profile,
        Uri officialUri,
        CancellationToken cancellationToken = default)
    {
        if (!BrowserOriginPolicy.IsOfficialChatGptStart(officialUri))
        {
            throw new BrowserAuthSessionException(
                "INTEGRATED_START_ORIGIN_REQUIRED",
                "Integrated User Mode chỉ được khởi chạy từ https://chatgpt.com/; URL mock hoặc đường dẫn đăng nhập trực tiếp đã bị chặn.");
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileDirectory) || !Directory.Exists(profile.ProfileDirectory))
        {
            throw new BrowserAuthSessionException(
                "INVALID_PROFILE_PATH",
                "Không thể truy cập thư mục Chrome profile được quản lý.",
                "ProfileDirectoryMissing");
        }

        var timing = new BrowserAuthTimingRecorder();
        var profileLeaseTimer = BrowserAuthTimingRecorder.StartStage();
        var ownership = _ownership.TryAcquire(profile.ProfileDirectory, BrowserProfileOwnerKind.Playwright);
        timing.Complete(BrowserAuthTimingStage.ProfileLease, profileLeaseTimer);
        if (!ownership.IsAcquired || ownership.Lease is null)
        {
            throw new BrowserAuthSessionException(ownership.Status, ownership.Message, "ProfileOwnership");
        }

        IPlaywright? playwright = null;
        IBrowserContext? context = null;
        PlaywrightBrowserAuthenticationSession? session = null;
        var lease = ownership.Lease;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = await _browserRuntime.GetStatusAsync(cancellationToken);
            if (!runtime.IsReady)
            {
                throw new BrowserAuthSessionException(
                    runtime.StatusCode,
                    runtime.Message,
                    "PlaywrightBrowserRuntime");
            }

            playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
            var launchTimer = BrowserAuthTimingRecorder.StartStage();
            try
            {
                context = await playwright.Chromium.LaunchPersistentContextAsync(
                    profile.ProfileDirectory,
                    new BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = false,
                        ChromiumSandbox = true,
                        Args = ["--new-window", "--no-first-run", "--no-default-browser-check", "--disable-sync"],
                        Timeout = 30_000
                    }).WaitAsync(cancellationToken);
            }
            finally
            {
                timing.Complete(BrowserAuthTimingStage.ChromeLaunch, launchTimer);
            }

            var page = context.Pages.Count > 0
                ? context.Pages[0]
                : await context.NewPageAsync().WaitAsync(cancellationToken);
            session = new PlaywrightBrowserAuthenticationSession(playwright, context, page, isAllowedOrigin: null, profileLease: lease, timing: timing);
            var navigationTimer = BrowserAuthTimingRecorder.StartStage();
            try
            {
                await session.NavigateAsync(officialUri, cancellationToken);
            }
            finally
            {
                timing.Complete(BrowserAuthTimingStage.OfficialNavigation, navigationTimer);
            }
            playwright = null;
            context = null;
            lease = null;
            return session;
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (context is not null)
            {
                await context.CloseAsync();
            }

            playwright?.Dispose();
            lease?.Dispose();
            throw;
        }
        catch (BrowserAuthSessionException)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (context is not null)
            {
                await context.CloseAsync();
            }

            playwright?.Dispose();
            lease?.Dispose();
            throw;
        }
        catch (TimeoutException exception)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (context is not null)
            {
                await context.CloseAsync();
            }

            playwright?.Dispose();
            lease?.Dispose();
            throw new BrowserAuthSessionException(
                "BROWSER_LAUNCH_TIMEOUT",
                "Chrome khởi động quá thời gian cho phép.",
                exception.GetType().Name);
        }
        catch (PlaywrightException exception)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (context is not null)
            {
                await context.CloseAsync();
            }

            playwright?.Dispose();
            lease?.Dispose();
            throw ClassifyLaunchFailure(profile, exception);
        }
    }

    private BrowserAuthSessionException ClassifyLaunchFailure(
        BrowserProfileSessionContext profile,
        PlaywrightException exception)
    {
        if (_lockInspector.Inspect(profile.ProfileDirectory) == BrowserProfileLockState.Active)
        {
            return new BrowserAuthSessionException(
                "PROFILE_IN_USE",
                "Chrome profile đang được sử dụng. Hãy đóng cửa sổ tương ứng hoặc tiếp tục đăng nhập thủ công trong Chrome.",
                exception.GetType().Name);
        }

        var message = exception.Message;
        if (message.Contains("driver", StringComparison.OrdinalIgnoreCase)
            || message.Contains("executable", StringComparison.OrdinalIgnoreCase))
        {
            return new BrowserAuthSessionException(
                "PLAYWRIGHT_DRIVER_MISSING",
                "Thiếu thành phần điều khiển trình duyệt.",
                exception.GetType().Name);
        }

        if (message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return new BrowserAuthSessionException(
                "PROFILE_PERMISSION_DENIED",
                "Không thể truy cập thư mục Chrome profile.",
                exception.GetType().Name);
        }

        return new BrowserAuthSessionException(
            "BROWSER_EXITED_EARLY",
            "Chrome đã đóng ngay sau khi khởi động.",
            exception.GetType().Name);
    }
}

public sealed class PlaywrightBrowserAuthenticationSession : IBrowserAuthenticationSession, IBrowserAuthenticationSessionDiagnostics, IBrowserIdentityObservationSession, IBrowserAccountObservationSession, IChatGptUsageApiObservationSession
{
    private static readonly TimeSpan BodyObservationBudget = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BodyReadAttemptTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AccountSettingsEvidenceBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AccountMenuClickTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LocatorProbeTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(125);
    private const string ChatGptUsageApiFetchScript = """
        async () => {
            const allowedOrigin = 'https://chatgpt.com';
            if (location.origin !== allowedOrigin || location.protocol !== 'https:') {
                return { status: 0, code: 'USAGE_API_ORIGIN_MISMATCH' };
            }

            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), 8000);
            try {
                const response = await fetch('/backend-api/wham/usage', {
                    method: 'GET',
                    credentials: 'same-origin',
                    cache: 'no-store',
                    redirect: 'error',
                    headers: { accept: 'application/json' },
                    signal: controller.signal
                });
                const contentLength = Number(response.headers.get('content-length') || 0);
                if (Number.isFinite(contentLength) && contentLength > 65536) {
                    return { status: response.status, code: 'USAGE_API_RESPONSE_TOO_LARGE' };
                }
                const text = await response.text();
                if (text.length > 65536) {
                    return { status: response.status, code: 'USAGE_API_RESPONSE_TOO_LARGE' };
                }
                if (!response.ok) {
                    return { status: response.status };
                }
                if (!(response.headers.get('content-type') || '').toLowerCase().includes('json')) {
                    return { status: response.status, code: 'USAGE_API_RESPONSE_NOT_JSON' };
                }

                const data = JSON.parse(text);
                const readWindow = (value) => {
                    if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
                    const numberOrNull = (candidate) => typeof candidate === 'number' && Number.isFinite(candidate) ? candidate : null;
                    const stringOrNumberOrNull = (candidate) => typeof candidate === 'string' || typeof candidate === 'number' ? candidate : null;
                    return {
                        used_percent: numberOrNull(value.used_percent),
                        limit_window_seconds: numberOrNull(value.limit_window_seconds),
                        reset_at: stringOrNumberOrNull(value.reset_at),
                        reset_after_seconds: numberOrNull(value.reset_after_seconds)
                    };
                };
                const candidateRateLimit = data && typeof data === 'object' ? (data.rate_limit || data.rate_limits) : null;
                const rateLimit = candidateRateLimit && typeof candidateRateLimit === 'object' ? candidateRateLimit : {};
                return {
                    status: response.status,
                    payload: {
                        plan_type: data && typeof data.plan_type === 'string' ? data.plan_type.slice(0, 64) : null,
                        rate_limit: {
                            primary_window: readWindow(rateLimit.primary_window),
                            secondary_window: readWindow(rateLimit.secondary_window)
                        }
                    }
                };
            } catch {
                return { status: 0, code: 'USAGE_API_REQUEST_FAILED' };
            } finally {
                clearTimeout(timeout);
            }
        }
        """;

    private static readonly string[] LoginEntryNames = ["Đăng nhập", "Log in", "Sign in"];
    private static readonly string[] EmailContinueNames =
    [
        "Continue",
        "Tiếp tục",
        "Next",
        "Tiếp theo",
        "Continue with email",
        "Tiếp tục với email",
        "Tiếp tục bằng email"
    ];

    private static readonly string[] CredentialContinueNames =
    [
        "Continue",
        "Tiếp tục",
        "Next",
        "Tiếp theo",
        "Verify",
        "Xác minh",
        "Sign in",
        "Log in",
        "Đăng nhập",
        "Xác nhận",
        "Gửi",
        "Submit"
    ];

    private static readonly string[] EmailAlternativeTokens =
    [
        "google",
        "apple",
        "microsoft",
        "phone",
        "số điện thoại",
        "sms",
        "sso"
    ];

    private static readonly string[] EmailSelectors =
    [
        "input#mobile-auth-email",
        "input[name='login_hint']",
        "input[name='username']",
        "input[name='email']",
        "input[autocomplete='email']",
        "input[autocomplete='username']",
        "input[aria-label*='email' i]",
        "input[placeholder*='email' i]",
        "input[type='email']",
        "input#email",
        "input#username"
    ];

    private static readonly string[] PasswordSelectors =
    [
        "input[name='password']",
        "input[autocomplete='current-password']",
        "input[type='password']",
        "input#password",
        "input#current-password",
        "input[name='current-password']",
        "input[aria-label*='password' i]",
        "input[aria-label*='mật khẩu' i]",
        "input[placeholder*='password' i]",
        "input[placeholder*='mật khẩu' i]",
        "input[data-testid*='password' i]"
    ];

    private static readonly string[] TotpSelectors =
    [
        "input[autocomplete='one-time-code']",
        "input[inputmode='numeric']",
        "input[aria-label*='code' i]",
        "input[name='code']"
    ];

    // Stable provider-specific account controls. Intentionally excludes generic
    // [aria-haspopup=menu] to avoid promoting a guest settings menu to auth.
    private static readonly string[] AuthenticatedAccountMenuSelectors =
    [
        "button[data-testid='account-menu']",
        "button[data-testid='profile-button']",
        "button[data-testid='user-menu']",
        "button[aria-label*='account menu' i]",
        "button[aria-label*='profile menu' i]",
        "button[aria-label*='user menu' i]",
        "button[aria-label*='tài khoản' i]",
        "button[aria-label*='hồ sơ' i]"
    ];

    private static readonly string[] SettingsNames =
    [
        "Settings",
        "Cài đặt",
        "Account settings",
        "Cài đặt tài khoản",
        "Settings and beta",
        "Settings & Beta",
        "Cài đặt và beta"
    ];

    private static readonly string[] AccountSectionNames = ["Account", "Tài khoản", "Profile", "Hồ sơ"];
    private static readonly string[] SettingsSurfaceSelectors =
    [
        "[role='dialog']",
        "[data-testid='modal']",
        "[data-testid='account-settings-dialog']",
        "[data-testid*='settings' i]",
        "[data-testid*='account' i][data-testid*='modal' i]",
        "[aria-label*='settings' i]",
        "[aria-label*='cài đặt' i]",
        "[role='tabpanel']",
        "main[aria-label*='settings' i]",
        "main[aria-label*='cài đặt' i]"
    ];

    private readonly IPlaywright _playwright;
    private readonly IBrowserContext _context;
    private IPage _page;
    private readonly Func<Uri?, bool> _isAllowedOrigin;
    private readonly IDisposable? _profileLease;
    private readonly BrowserAuthTimingRecorder _timing;
    private bool _disposed;

    public PlaywrightBrowserAuthenticationSession(
        IPlaywright playwright,
        IBrowserContext context,
        IPage page,
        Func<Uri?, bool>? isAllowedOrigin = null,
        IDisposable? profileLease = null)
        : this(playwright, context, page, isAllowedOrigin, profileLease, null)
    {
    }

    internal PlaywrightBrowserAuthenticationSession(
        IPlaywright playwright,
        IBrowserContext context,
        IPage page,
        Func<Uri?, bool>? isAllowedOrigin,
        IDisposable? profileLease,
        BrowserAuthTimingRecorder? timing)
    {
        _playwright = playwright;
        _context = context;
        _page = page;
        _isAllowedOrigin = isAllowedOrigin ?? BrowserOriginPolicy.IsAllowed;
        _profileLease = profileLease;
        _timing = timing ?? new BrowserAuthTimingRecorder();
    }

    public BrowserAuthTimingSnapshot Timing => _timing.Snapshot();

    public Uri CurrentUri
        => Uri.TryCreate(_page.Url, UriKind.Absolute, out var uri)
            ? uri
            : new Uri("about:blank", UriKind.Absolute);

    public async Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_isAllowedOrigin(uri))
        {
            throw new BrowserAuthSessionException(
                "BROWSER_ORIGIN_NOT_ALLOWED",
                "Chỉ được điều hướng tới origin chính thức đã được cho phép.");
        }

        try
        {
            await _page.GotoAsync(uri.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000
            }).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            throw new BrowserAuthSessionException("BROWSER_NAVIGATION_FAILED", "Không thể điều hướng tới website chính thức.");
        }

            if (!_isAllowedOrigin(CurrentUri))
            {
                throw new BrowserAuthSessionException(
                    "BROWSER_ORIGIN_UNTRUSTED",
                    "Đã chặn chuyển hướng vì website đã rời khỏi origin chính thức cho phép.");
            }
    }

    public async Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            SelectLatestInspectablePage();

            var currentUri = CurrentUri;
            var body = await ReadBodyTextWithRetryAsync(cancellationToken);
            if (!body.IsSuccess)
            {
                return new BrowserPageSnapshot(
                    currentUri,
                    BrowserPageState.Unknown,
                    IsPageReady: false,
                    ObservationFailureCode: body.FailureCode,
                    ObservationFailureMessage: body.FailureMessage);
            }

            var bodyText = body.Text;
            var emailField = await FindFirstVisibleAsync(EmailSelectors, cancellationToken);
            var emailScope = emailField is null
                ? null
                : await FindCredentialScopeAsync(emailField, cancellationToken);
            var emailContinueActions = emailScope is null
                ? []
                : await FindVisibleContinueActionsAsync(emailScope, EmailContinueNames, cancellationToken);
            var isEmailModal = emailField is not null
                && await IsModalScopeAsync(emailField, cancellationToken);
            var hasEmail = emailField is not null;
            var hasPassword = await HasVisibleAsync(PasswordSelectors, cancellationToken);
            var hasTotp = await HasVisibleAsync(TotpSelectors, cancellationToken);
            var loginEntries = await FindVisibleLoginEntriesAsync(cancellationToken);
            var pageReady = await IsPageReadyAsync(cancellationToken);
            var state = DetectPageState(bodyText, hasEmail, hasPassword, hasTotp, loginEntries.Count, pageReady);
            var mfaObservation = ClassifyMfaObservation(bodyText, state, hasTotp, pageReady);
            // An authenticated ChatGPT shell may show an account/avatar menu
            // without rendering "Sign out" until that menu is expanded.
            // Only upgrade a ready, otherwise Unknown *official ChatGPT* page;
            // never override an interactive login/challenge state.
            if (state == BrowserPageState.Unknown
                && pageReady
                && !hasEmail && !hasPassword && !hasTotp
                && loginEntries.Count == 0
                && currentUri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                && await HasVisibleAsync(AuthenticatedAccountMenuSelectors, cancellationToken))
            {
                state = BrowserPageState.Authenticated;
            }
            return new BrowserPageSnapshot(
                currentUri,
                state,
                hasEmail,
                hasPassword,
                hasTotp,
                HasGuestLoginEntry: loginEntries.Count > 0,
                GuestLoginEntryCount: loginEntries.Count,
                IsPageReady: pageReady,
                EmailContinueActionCount: emailContinueActions.Count,
                    IsEmailModal: isEmailModal,
                    MfaObservation: mfaObservation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new BrowserPageSnapshot(
                CurrentUri,
                BrowserPageState.Unknown,
                IsPageReady: false,
                ObservationFailureCode: "BROWSER_DOM_READ_TIMEOUT",
                ObservationFailureMessage: "Provider đang chuyển trạng thái; sẽ thử lại trong deadline quan sát.");
        }
        catch (PlaywrightException exception) when (IsTransientObservationFailure(exception))
        {
            return new BrowserPageSnapshot(
                CurrentUri,
                BrowserPageState.Unknown,
                IsPageReady: false,
                ObservationFailureCode: IsNavigationFailure(exception)
                    ? "BROWSER_NAVIGATION_IN_PROGRESS"
                    : "BROWSER_DOM_READ_TRANSIENT",
                ObservationFailureMessage: "DOM provider đang thay đổi; sẽ thử lại trong deadline quan sát.");
        }
        catch (PlaywrightException)
        {
            throw new BrowserAuthSessionException("BROWSER_CLOSED", "Chrome đã đóng hoặc phiên trình duyệt không còn khả dụng.");
        }
    }

    public async Task<BrowserIdentityEvidence> ReadIdentityEvidenceAsync(CancellationToken cancellationToken = default)
    {
        var observation = await ReadAccountObservationAsync(
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            cancellationToken);
        return new BrowserIdentityEvidence(
            observation.AuthenticatedShellDetected,
            observation.AccountMenuDetected,
            observation.Email.Value,
            observation.DisplayName.Value,
            observation.EvidenceSurface,
            observation.Email.FailureReasonCode ?? observation.FailureReasonCode,
            observation.ObservedAtUtc,
            observation);
    }

    public async Task<ChatGptUsageApiReadResult> ReadUsageApiAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SelectLatestInspectablePage();
        if (CurrentUri.Scheme != Uri.UriSchemeHttps
            || !CurrentUri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatGptUsageApiReadResult(
                false,
                "USAGE_API_ORIGIN_MISMATCH",
                "Chỉ được gọi endpoint usage khi session đang ở https://chatgpt.com.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _page.EvaluateAsync<JsonElement>(ChatGptUsageApiFetchScript);
            cancellationToken.ThrowIfCancellationRequested();
            return ChatGptUsageApiResponseParser.Parse(result, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlaywrightException exception) when (IsClosedFailure(exception))
        {
            throw new BrowserAuthSessionException("BROWSER_CLOSED", "Chrome đã đóng hoặc phiên browser không còn khả dụng.");
        }
        catch (PlaywrightException)
        {
            return new ChatGptUsageApiReadResult(
                false,
                "USAGE_API_REQUEST_FAILED",
                "Không thể đọc usage từ session ChatGPT hiện tại; không lưu phản hồi thô.");
        }
    }

    public async Task<ProviderAccountObservation> ReadAccountObservationAsync(
        Guid accountId,
        Guid attemptId,
        Guid browserProfileBindingId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            SelectLatestInspectablePage();
            if (!_isAllowedOrigin(CurrentUri))
            {
                throw new BrowserAuthSessionException(
                    "IDENTITY_ORIGIN_UNTRUSTED",
                    "Không thể đọc dữ liệu tài khoản vì browser đã rời origin được cho phép.");
            }

            var body = await ReadBodyTextWithRetryAsync(cancellationToken);
            if (!body.IsSuccess)
            {
                throw new BrowserAuthSessionException(
                    body.FailureCode ?? "OBSERVATION_TIMED_OUT",
                    body.FailureMessage ?? "Không thể đọc giao diện provider trong thời gian cho phép.");
            }

            var now = DateTimeOffset.UtcNow;
            var isOfficialAccountSettingsPage = IsOfficialChatGptAccountSettingsPage(CurrentUri);
            var pageEmailEvidence = ExtractEmailEvidence(body.Text);
            var hasOfficialAccountSettingsEvidence = isOfficialAccountSettingsPage
                && pageEmailEvidence.Email is not null
                && Matches(body.Text, "email|e-mail|trạng thái tài khoản|account status");
            var authenticated = Matches(
                body.Text,
                "log out|sign out|account settings|manage account|my account|đăng xuất|cài đặt tài khoản|quản lý tài khoản|tài khoản của tôi")
                || (CurrentUri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                    && await HasVisibleAsync(AuthenticatedAccountMenuSelectors, cancellationToken))
                || hasOfficialAccountSettingsEvidence;

            var accountControl = await FindFirstVisibleAsync(AuthenticatedAccountMenuSelectors, cancellationToken);
            var accountMenuDetected = accountControl is not null;
            var evidenceSurfaces = new List<string>();
            evidenceSurfaces.Add(accountMenuDetected
                ? "Visible provider account-menu"
                : "Authenticated provider shell");
            var menuText = accountControl is null ? null : await ReadLocatorTextAsync(accountControl, cancellationToken);
            var displayName = await ReadDisplayNameAsync(accountControl, menuText, cancellationToken);
            var menuEmailEvidence = ExtractEmailEvidence(menuText);
            var observedEmail = menuEmailEvidence.Email;
            var emailFailureCode = menuEmailEvidence.FailureCode;
            var menuOpened = false;

            if (accountControl is not null && observedEmail is null)
            {
                menuOpened = await TryOpenAccountMenuAsync(accountControl, cancellationToken);
                var menu = await FindVisibleAccountSurfaceAsync(cancellationToken);
                if (menu is not null)
                {
                    menuText = await ReadLocatorTextAsync(menu, cancellationToken);
                    menuEmailEvidence = ExtractEmailEvidence(menuText);
                    observedEmail = menuEmailEvidence.Email;
                    emailFailureCode = menuEmailEvidence.FailureCode;
                    displayName ??= await ReadDisplayNameAsync(menu, menuText, cancellationToken);
                }
            }

            var settingsOpened = false;
            var settingsAlreadyOpen = hasOfficialAccountSettingsEvidence;
            ILocator? settingsSurface = settingsAlreadyOpen ? _page.Locator("body") : null;
            var settingsFailure = (string?)null;
            try
            {
                if (settingsAlreadyOpen)
                {
                    evidenceSurfaces.Add("official ChatGPT account settings UI");
                }
                else
                {
                    if (IsOfficialChatGptSettingsRoute(CurrentUri))
                    {
                        // A provider may already be on a settings subpage such as
                        // /settings/general-settings. Navigate directly to the
                        // official account section instead of mistaking the
                        // existing settings shell for the account page.
                        settingsOpened = await NavigateToOfficialAccountSettingsAsync(cancellationToken);
                        settingsAlreadyOpen = settingsOpened && IsOfficialChatGptAccountSettingsPage(CurrentUri);
                    }
                    else
                    {
                        if (!menuOpened && accountControl is not null)
                        {
                            menuOpened = await TryOpenAccountMenuAsync(accountControl, cancellationToken);
                        }

                        settingsOpened = await TryOpenSettingsAsync(cancellationToken);
                        if (!settingsOpened && authenticated)
                        {
                            // ChatGPT's account-menu markup changes over time. If its
                            // visible settings entry cannot be resolved, use the
                            // provider's official read-only account route instead.
                            settingsOpened = await NavigateToOfficialAccountSettingsAsync(cancellationToken);
                            settingsAlreadyOpen = settingsOpened
                                && IsOfficialChatGptAccountSettingsPage(CurrentUri);
                        }
                    }

                    if (settingsOpened)
                    {
                        settingsSurface = settingsAlreadyOpen
                            ? _page.Locator("body")
                            : await WaitForSettingsSurfaceAsync(cancellationToken);
                        if (settingsSurface is null)
                        {
                            settingsFailure = "ACCOUNT_SETTINGS_UNAVAILABLE";
                            evidenceSurfaces.Add("official Settings UI unavailable");
                        }
                        else
                        {
                            evidenceSurfaces.Add("official Settings UI");
                            await TryOpenAccountSectionAsync(settingsSurface, cancellationToken);
                            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                        }
                    }
                    else
                    {
                        settingsFailure = "ACCOUNT_SETTINGS_UNAVAILABLE";
                        evidenceSurfaces.Add("official Settings UI unavailable");
                    }
                }

                var settingsText = settingsSurface is null
                    ? string.Empty
                    : await ReadLocatorTextAsync(settingsSurface, cancellationToken) ?? string.Empty;
                var settingsEmailEvidence = ExtractEmailEvidence(settingsText);
                var settingsEmail = settingsEmailEvidence.Email;
                if (observedEmail is null && settingsEmail is not null)
                {
                    observedEmail = settingsEmail;
                    emailFailureCode = null;
                }
                else if (observedEmail is null)
                {
                    emailFailureCode = settingsEmailEvidence.FailureCode ?? emailFailureCode;
                }

                var settingsDisplayName = await ReadDisplayNameAsync(
                    settingsSurface,
                    settingsAlreadyOpen ? null : settingsText,
                    cancellationToken);
                displayName ??= settingsDisplayName;

                var emailEvidenceSource = settingsEmail is not null
                    ? "OfficialSettingsAccount"
                    : observedEmail is not null
                        ? "AccountMenu"
                        : "AccountMenu";
                var emailEvidenceType = settingsEmail is not null
                    ? "OfficialSettingsAccount"
                    : "AccountMenu";
                var displayNameEvidenceSource = settingsSurface is not null
                    ? "OfficialAccountUI"
                    : "AccountMenu";
                var emailField = Field(
                    ProviderAccountObservationField.Email,
                    observedEmail,
                    observedEmail is null ? emailFailureCode ?? "IDENTITY_EMAIL_NOT_EXPOSED" : null,
                    observedEmail is null ? ProviderAccountObservationFieldStatus.Unavailable : ProviderAccountObservationFieldStatus.Observed,
                    emailEvidenceSource,
                    emailEvidenceType);
                var displayNameField = Field(
                    ProviderAccountObservationField.DisplayName,
                    displayName,
                    displayName is null ? "DISPLAY_NAME_NOT_EXPOSED" : null,
                    displayName is null ? ProviderAccountObservationFieldStatus.Unavailable : ProviderAccountObservationFieldStatus.Observed,
                    displayNameEvidenceSource,
                    "OfficialAccountUI");
                var plan = ExtractPlanLabel(settingsText);
                var mfa = settingsAlreadyOpen ? null : ExtractMfaLabel(settingsText);
                var planField = Field(
                    ProviderAccountObservationField.Plan,
                    plan,
                    plan is null ? "PLAN_LABEL_UNAVAILABLE" : null,
                    plan is null ? ProviderAccountObservationFieldStatus.Unavailable : ProviderAccountObservationFieldStatus.Observed,
                    plan is null ? "Official Account settings did not expose a supported plan label." : "Official Account settings subscription label.",
                    "OfficialAccountOrPlanUI");
                var mfaField = Field(
                    ProviderAccountObservationField.MfaConfiguration,
                    mfa,
                    mfa is null ? "MFA_CONFIGURATION_UNAVAILABLE" : null,
                    mfa is null ? ProviderAccountObservationFieldStatus.Unavailable : ProviderAccountObservationFieldStatus.Observed,
                    mfa is null ? "Official security/account UI did not expose a supported MFA status." : "Official security/account UI label.",
                    "OfficialSecuritySettings");
                var usageField = Field(
                    ProviderAccountObservationField.UsageQuota,
                    null,
                    "UNSUPPORTED_USAGE_SOURCE",
                    ProviderAccountObservationFieldStatus.Unsupported,
                    "No supported ChatGPT Web usage or quota source was exposed by the designated account surfaces.",
                    "OfficialAccountOrPlanUI");

                var overallStatus = !authenticated
                    ? "AuthenticationNotConfirmed"
                    : settingsFailure is not null
                        ? "PartiallyResolved"
                        : "Observed";
                return new ProviderAccountObservation(
                    accountId,
                    attemptId,
                    browserProfileBindingId,
                    "openai-chatgpt",
                    "managed-playwright-context",
                    now,
                    DetectLocale(menuText, settingsText),
                    authenticated,
                    accountMenuDetected,
                    overallStatus,
                    string.Join("; ", evidenceSurfaces),
                    settingsFailure ?? emailField.FailureReasonCode,
                    displayNameField,
                    emailField,
                    planField,
                    mfaField,
                    usageField);
            }
            finally
            {
                if (settingsOpened || menuOpened)
                {
                    await CloseAccountSurfacesAsync(cancellationToken);
                }
            }
        }
        catch (BrowserAuthSessionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException exception)
        {
            throw new BrowserAuthSessionException(
                IsClosedFailure(exception) ? "BROWSER_CLOSED" : "PROVIDER_UI_CHANGED",
                IsClosedFailure(exception)
                    ? "Chrome đã đóng hoặc phiên trình duyệt không còn khả dụng."
                    : "Giao diện account/settings của provider đã thay đổi hoặc không thể đọc an toàn.",
                exception.GetType().Name);
        }
    }

    public async Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_isAllowedOrigin(CurrentUri))
        {
            throw new BrowserAuthSessionException(
                "BROWSER_ORIGIN_UNTRUSTED",
                "Đã chặn điều hướng vì Chrome không còn ở origin chính thức.");
        }

        var entries = await FindVisibleLoginEntriesAsync(cancellationToken);
        if (entries.Count == 0)
        {
            throw new BrowserAuthSessionException(
                "LOGIN_ENTRY_NOT_FOUND",
                "Không còn tìm thấy nút Đăng nhập an toàn trên trang hiện tại.");
        }

        if (entries.Count != 1)
        {
            throw new BrowserAuthSessionException(
                "LOGIN_ENTRY_AMBIGUOUS",
                "Có nhiều nút Đăng nhập phù hợp; đã dừng để tránh chọn nhầm.");
        }

        var popup = new TaskCompletionSource<IPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<IPage> pageHandler = (_, page) => popup.TrySetResult(page);
        _context.Page += pageHandler;
        try
        {
            await entries[0].ClickAsync(new LocatorClickOptions { Timeout = 5_000 }).WaitAsync(cancellationToken);

            var popupWait = Task.WhenAny(
                popup.Task,
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
            var completed = await popupWait.WaitAsync(cancellationToken);
            if (completed == popup.Task)
            {
                _page = await popup.Task.WaitAsync(cancellationToken);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pages = _context.Pages;
                var latestPage = pages.Count > 0 ? pages[^1] : null;
                if (latestPage is not null && !ReferenceEquals(latestPage, _page))
                {
                    _page = latestPage;
                }
            }

            try
            {
                await _page.WaitForLoadStateAsync(
                    LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions { Timeout = 5_000 }).WaitAsync(cancellationToken);
            }
            catch (TimeoutException)
            {
                // The authentication page may still be loading; ReadPageAsync performs the bounded follow-up read.
            }

            if (!_isAllowedOrigin(CurrentUri))
            {
                throw new BrowserAuthSessionException(
                    "BROWSER_ORIGIN_UNTRUSTED",
                    "Đã chặn điều hướng vì trang xác thực không thuộc origin chính thức.");
            }
        }
        catch (BrowserAuthSessionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            throw new BrowserAuthSessionException(
                "LOGIN_ENTRY_CLICK_FAILED",
                "Không thể kích hoạt nút Đăng nhập trong Chrome.");
        }
        finally
        {
            _context.Page -= pageHandler;
        }
    }

    public async Task FillAsync(
        BrowserAuthField field,
        string value,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(value))
        {
            throw new BrowserAuthSessionException("BROWSER_FIELD_VALUE_EMPTY", "Không có giá trị hợp lệ để điền vào biểu mẫu.");
        }

        if (!_isAllowedOrigin(CurrentUri))
        {
            throw new BrowserAuthSessionException(
                "BROWSER_ORIGIN_UNTRUSTED",
                "Đã chặn việc điền dữ liệu vì trình duyệt không còn ở origin chính thức.");
        }

        var selectors = field switch
        {
            BrowserAuthField.Email => EmailSelectors,
            BrowserAuthField.Password => PasswordSelectors,
            BrowserAuthField.Totp => TotpSelectors,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        try
        {
            var locator = await WaitForFirstVisibleAsync(selectors, TimeSpan.FromSeconds(5), cancellationToken);
            if (locator is null)
            {
                throw new BrowserAuthSessionException("BROWSER_FIELD_NOT_FOUND", "Không còn nhận diện được trường xác thực an toàn.");
            }

            await locator.FillAsync(value, new LocatorFillOptions { Timeout = 5_000 }).WaitAsync(cancellationToken);
            var valueLength = (await locator.InputValueAsync().WaitAsync(cancellationToken)).Length;
            if (valueLength == 0)
            {
                throw new BrowserAuthSessionException("BROWSER_FILL_FAILED", "Không thể xác minh trường đã được điền.");
            }
        }
        catch (BrowserAuthSessionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            throw new BrowserAuthSessionException("BROWSER_FILL_FAILED", "Không thể điền trường xác thực trong Chrome.");
        }
    }

    public async Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        => await ActivateCredentialContinueAsync(
            EmailSelectors,
            EmailContinueNames,
            "EMAIL_FIELD_NOT_FOUND",
            "Không còn nhận diện được trường email trước khi kích hoạt bước tiếp theo.",
            "EMAIL_CONTINUE_NOT_FOUND",
            "Không tìm thấy nút Tiếp tục dành riêng cho biểu mẫu email.",
            "EMAIL_CONTINUE_AMBIGUOUS",
            "Có nhiều nút Tiếp tục trong biểu mẫu email; đã dừng để tránh chọn nhầm.",
            "EMAIL_CONTINUE_CLICK_FAILED",
            "Không thể kích hoạt nút Tiếp tục của biểu mẫu email trong Chrome.",
            cancellationToken);

    public async Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        => await ActivateCredentialContinueAsync(
            PasswordSelectors,
            CredentialContinueNames,
            "PASSWORD_FIELD_NOT_FOUND",
            "Không còn nhận diện được trường mật khẩu trước khi gửi bước tiếp theo.",
            "PASSWORD_CONTINUE_NOT_FOUND",
            "Không tìm thấy nút tiếp tục của biểu mẫu mật khẩu.",
            "PASSWORD_CONTINUE_AMBIGUOUS",
            "Có nhiều nút tiếp tục trong biểu mẫu mật khẩu; đã dừng để tránh chọn nhầm.",
            "PASSWORD_CONTINUE_CLICK_FAILED",
            "Không thể kích hoạt nút tiếp tục của biểu mẫu mật khẩu trong Chrome.",
            cancellationToken);

    public async Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default)
        => await ActivateCredentialContinueAsync(
            TotpSelectors,
            CredentialContinueNames,
            "TOTP_FIELD_NOT_FOUND",
            "Không còn nhận diện được trường Authenticator trước khi gửi mã.",
            "TOTP_CONTINUE_NOT_FOUND",
            "Không tìm thấy nút xác nhận của biểu mẫu Authenticator.",
            "TOTP_CONTINUE_AMBIGUOUS",
            "Có nhiều nút xác nhận trong biểu mẫu Authenticator; đã dừng để tránh chọn nhầm.",
            "TOTP_CONTINUE_CLICK_FAILED",
            "Không thể kích hoạt nút xác nhận của biểu mẫu Authenticator trong Chrome.",
            cancellationToken);

    private async Task ActivateCredentialContinueAsync(
        string[] fieldSelectors,
        string[] continueNames,
        string fieldNotFoundCode,
        string fieldNotFoundMessage,
        string continueNotFoundCode,
        string continueNotFoundMessage,
        string continueAmbiguousCode,
        string continueAmbiguousMessage,
        string clickFailedCode,
        string clickFailedMessage,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_isAllowedOrigin(CurrentUri))
        {
            throw new BrowserAuthSessionException(
                "BROWSER_ORIGIN_UNTRUSTED",
                "Đã chặn thao tác tiếp tục vì Chrome không còn ở origin chính thức.");
        }

        try
        {
            var field = await WaitForFirstVisibleAsync(fieldSelectors, TimeSpan.FromSeconds(5), cancellationToken);
            if (field is null)
            {
                throw new BrowserAuthSessionException(
                    fieldNotFoundCode,
                    fieldNotFoundMessage);
            }

            var scope = await FindCredentialScopeAsync(field, cancellationToken);
            var actions = await FindVisibleContinueActionsAsync(scope, continueNames, cancellationToken);
            if (actions.Count == 0)
            {
                throw new BrowserAuthSessionException(
                    continueNotFoundCode,
                    continueNotFoundMessage);
            }

            if (actions.Count != 1)
            {
                throw new BrowserAuthSessionException(
                    continueAmbiguousCode,
                    continueAmbiguousMessage);
            }

            await actions[0].ClickAsync(new LocatorClickOptions { Timeout = 5_000 }).WaitAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);

            if (!_isAllowedOrigin(CurrentUri))
            {
                throw new BrowserAuthSessionException(
                    "BROWSER_ORIGIN_UNTRUSTED",
                    "Đã chặn chuyển hướng sau bước xác thực vì trang không thuộc origin chính thức.");
            }
        }
        catch (BrowserAuthSessionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            throw new BrowserAuthSessionException(
                clickFailedCode,
                clickFailedMessage);
        }
    }

    private async Task<bool> HasVisibleAsync(string[] selectors, CancellationToken cancellationToken)
        => await FindFirstVisibleAsync(selectors, cancellationToken) is not null;

    private void SelectLatestInspectablePage()
    {
        var pages = _context.Pages;
        for (var index = pages.Count - 1; index >= 0; index--)
        {
            var candidate = pages[index];
            try
            {
                if (Uri.TryCreate(candidate.Url, UriKind.Absolute, out var candidateUri)
                    && _isAllowedOrigin(candidateUri))
                {
                    _page = candidate;
                    return;
                }
            }
            catch (PlaywrightException)
            {
                // A closed or transitioning tab is skipped; the next active tab may still be inspectable.
            }
        }
    }

    private async Task<string?> TryReadAccountPopoverEmailAsync(CancellationToken cancellationToken)
    {
        var buttons = _page.Locator(string.Join(", ", AuthenticatedAccountMenuSelectors));
        var count = await buttons.CountAsync().WaitAsync(cancellationToken);
        for (var index = 0; index < Math.Min(count, 12); index++)
        {
            var button = buttons.Nth(index);
            if (!await button.IsVisibleAsync().WaitAsync(cancellationToken))
            {
                continue;
            }

            try
            {
                await button.ClickAsync(new LocatorClickOptions { Timeout = 1_200 })
                    .WaitAsync(cancellationToken);
                var popover = _page.Locator(
                    "[role='menu'], [data-testid='account-menu-content'], [data-testid='profile-menu-content']").First;
                await popover.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 1_200
                }).WaitAsync(cancellationToken);
                return ExtractEmail(await popover.InnerTextAsync().WaitAsync(cancellationToken));
            }
            catch (PlaywrightException)
            {
                // Menu UI is optional and can vary by provider version.
                // No identity evidence is safer than reading arbitrary page text.
            }
            finally
            {
                try
                {
                    await _page.Keyboard.PressAsync("Escape").WaitAsync(cancellationToken);
                }
                catch (PlaywrightException)
                {
                    // A closed/transitioning popover does not invalidate the session.
                }
            }
        }

        return null;
    }

    private async Task<bool> TryOpenAccountMenuAsync(
        ILocator accountControl,
        CancellationToken cancellationToken)
    {
        try
        {
            await accountControl.ClickAsync(new LocatorClickOptions
            {
                Timeout = (float)AccountMenuClickTimeout.TotalMilliseconds
            }).WaitAsync(cancellationToken);
            return await WaitForVisibleAccountSurfaceAsync(cancellationToken) is not null;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private async Task<ILocator?> FindVisibleAccountSurfaceAsync(CancellationToken cancellationToken)
    {
        var surfaces = _page.Locator(
            "[role='menu'], [data-testid='account-menu-content'], [data-testid='profile-menu-content'], "
            + "[data-testid*='account-menu' i], [data-testid*='profile-menu' i]");
        var count = await surfaces.CountAsync().WaitAsync(cancellationToken);
        for (var index = 0; index < count; index++)
        {
            var candidate = surfaces.Nth(index);
            if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<ILocator?> WaitForVisibleAccountSurfaceAsync(
        CancellationToken cancellationToken,
        TimeSpan? budget = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(budget ?? TimeSpan.FromSeconds(2));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var surface = await FindVisibleAccountSurfaceAsync(cancellationToken);
            if (surface is not null)
            {
                return surface;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        return null;
    }

    private async Task<bool> TryOpenSettingsAsync(CancellationToken cancellationToken)
    {
        var menu = await FindVisibleAccountSurfaceAsync(cancellationToken);
        foreach (var name in SettingsNames)
        {
            var candidate = menu is null
                ? _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name, Exact = true })
                : menu.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name, Exact = true });
            var count = await candidate.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                var visible = candidate.Nth(index);
                if (!await visible.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    continue;
                }

                try
                {
                    if (await ClickSettingsCandidateAsync(visible, cancellationToken))
                    {
                        return true;
                    }
                }
                catch (PlaywrightException)
                {
                    // Try the next semantic match; no arbitrary DOM click is used.
                }
            }

            var menuItemCandidate = menu is null
                ? _page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = name, Exact = true })
                : menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = name, Exact = true });
            count = await menuItemCandidate.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                var visible = menuItemCandidate.Nth(index);
                if (!await visible.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    continue;
                }

                try
                {
                    if (await ClickSettingsCandidateAsync(visible, cancellationToken))
                    {
                        return true;
                    }
                }
                catch (PlaywrightException)
                {
                    // Try the next semantic match; no arbitrary DOM click is used.
                }
            }

            var textCandidate = menu is null
                ? _page.GetByText(name, new PageGetByTextOptions { Exact = true })
                : menu.GetByText(name, new LocatorGetByTextOptions { Exact = true });
            count = await textCandidate.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                var visible = textCandidate.Nth(index);
                if (!await visible.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    continue;
                }

                try
                {
                    if (await ClickSettingsCandidateAsync(visible, cancellationToken))
                    {
                        return true;
                    }
                }
                catch (PlaywrightException)
                {
                    // The provider may render a non-clickable text node.
                }
            }
        }

        if (menu is not null)
        {
            // Provider rollouts sometimes expose a semantic settings control
            // with a stable test id or aria label but no role-name mapping.
            var boundedSettingsFallback = _page.Locator(
                "[data-testid*='settings' i], [aria-label*='settings' i], [aria-label*='cài đặt' i], "
                + "a[href*='/settings' i], [role='menuitem'] a[href*='/settings' i]");
            var fallbackCount = await boundedSettingsFallback.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < fallbackCount; index++)
            {
                var candidate = boundedSettingsFallback.Nth(index);
                if (!await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    continue;
                }

                try
                {
                    if (await ClickSettingsCandidateAsync(candidate, cancellationToken))
                    {
                        return true;
                    }
                }
                catch (PlaywrightException)
                {
                    // Try the next bounded semantic candidate.
                }
            }
        }

        return false;
    }

    private async Task<bool> NavigateToOfficialAccountSettingsAsync(CancellationToken cancellationToken)
    {
        var currentUri = CurrentUri;
        if (currentUri is null || !_isAllowedOrigin(currentUri))
        {
            return false;
        }

        try
        {
            var accountSettingsUri = new Uri(currentUri, "/settings/account");
            if (!_isAllowedOrigin(accountSettingsUri))
            {
                return false;
            }

            await _page.GotoAsync(
                    accountSettingsUri.AbsoluteUri,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 12_000
                    })
                .WaitAsync(cancellationToken);
            if (!IsOfficialChatGptAccountSettingsPage(CurrentUri))
            {
                return false;
            }

            return await WaitForOfficialAccountSettingsEvidenceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private async Task<bool> WaitForOfficialAccountSettingsEvidenceAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(AccountSettingsEvidenceBudget);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var body = await _page.EvaluateAsync<string?>(
                        "() => document.body?.innerText || document.body?.textContent || ''")
                    .WaitAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(body)
                    && ExtractEmailEvidence(body).Email is not null
                    && Matches(body, "email|e-mail|trạng thái tài khoản|account status"))
                {
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlaywrightException exception) when (IsClosedFailure(exception))
            {
                return false;
            }
            catch (PlaywrightException)
            {
                // A provider SPA can replace the document while account details hydrate.
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(200, remaining.TotalMilliseconds)), cancellationToken);
        }

        return false;
    }

    private async Task<bool> ClickSettingsCandidateAsync(
        ILocator candidate,
        CancellationToken cancellationToken)
    {
        await candidate.ClickAsync(new LocatorClickOptions { Timeout = 1_500 }).WaitAsync(cancellationToken);
        return await WaitForSettingsSurfaceAsync(
            cancellationToken,
            TimeSpan.FromSeconds(4)) is not null;
    }

    private async Task<ILocator?> WaitForSettingsSurfaceAsync(
        CancellationToken cancellationToken,
        TimeSpan? budget = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(budget ?? TimeSpan.FromSeconds(5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var selector in SettingsSurfaceSelectors)
            {
                var candidate = _page.Locator(selector).First;
                try
                {
                    if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken)
                        && await IsSettingsSurfaceCandidateAsync(candidate, cancellationToken))
                    {
                        return candidate;
                    }
                }
                catch (PlaywrightException)
                {
                    // Navigation can replace the settings surface between probes.
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }

        return null;
    }

    private async Task<bool> IsSettingsSurfaceCandidateAsync(
        ILocator candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            var isContainer = await candidate.EvaluateAsync<bool>(
                "element => !['BUTTON', 'A'].includes(element.tagName) && !element.closest('[role=\\\"menu\\\"]')")
                .WaitAsync(cancellationToken);
            if (!isContainer)
            {
                return false;
            }

            var text = await ReadLocatorTextAsync(candidate, cancellationToken) ?? string.Empty;
            var isSettingsRoute = CurrentUri.AbsolutePath.Contains("settings", StringComparison.OrdinalIgnoreCase)
                || CurrentUri.Fragment.Contains("settings", StringComparison.OrdinalIgnoreCase);
            return isSettingsRoute
                || Matches(text, "settings|account|profile|security|subscription|plan|cài đặt|tài khoản|hồ sơ|bảo mật|gói đăng ký|gói của tôi");
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private async Task TryOpenAccountSectionAsync(
        ILocator settingsSurface,
        CancellationToken cancellationToken)
    {
        foreach (var name in AccountSectionNames)
        {
            var tabs = settingsSurface.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name, Exact = true });
            var count = await tabs.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                var tab = tabs.Nth(index);
                if (!await tab.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    continue;
                }

                try
                {
                    await tab.ClickAsync(new LocatorClickOptions { Timeout = 1_200 }).WaitAsync(cancellationToken);
                    return;
                }
                catch (PlaywrightException)
                {
                    // Account may already be the selected section.
                }
            }
        }
    }

    private async Task CloseAccountSurfacesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _page.Keyboard.PressAsync("Escape").WaitAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        catch (PlaywrightException)
        {
            // Closing a transient dialog is best effort and never changes data.
        }
    }

    private static async Task<string?> ReadDisplayNameAsync(
        ILocator? surface,
        string? surfaceText,
        CancellationToken cancellationToken)
    {
        if (surface is not null)
        {
            var named = surface.Locator(
                "[data-testid*='account-name' i], [data-testid*='profile-name' i], [data-testid*='user-name' i], "
                + "[aria-label*='account name' i], [aria-label*='profile name' i]");
            var count = await named.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                var candidate = named.Nth(index);
                if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    var text = await ReadLocatorTextAsync(candidate, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(surfaceText))
        {
            return null;
        }

        foreach (var line in surfaceText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ExtractEmail(line) is null
                && !Matches(line, "settings|account|profile|log out|sign out|đăng xuất|cài đặt|tài khoản|hồ sơ|upgrade|manage"))
            {
                return line.Length <= 100 ? line : null;
            }
        }

        return null;
    }

    private static async Task<string?> ReadLocatorTextAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        try
        {
            var innerText = await locator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1_500 }).WaitAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(innerText))
            {
                return innerText;
            }
        }
        catch (TimeoutException)
        {
            // Some provider surfaces are replaced while their dialog remains visible.
        }
        catch (PlaywrightException)
        {
            // Some provider surfaces are replaced while their dialog remains visible.
            // A bounded text-content read can still capture the designated surface.
        }

        try
        {
            var textContent = await locator.TextContentAsync(new LocatorTextContentOptions { Timeout = 1_500 }).WaitAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                return textContent;
            }
        }
        catch (TimeoutException)
        {
            // Fall through to the final bounded read.
        }
        catch (PlaywrightException)
        {
            // Fall through to a final read of the already-bounded locator.
        }

        try
        {
            return await locator.EvaluateAsync<string>(
                "element => element.innerText || element.textContent || ''").WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadLocatorAttributeAsync(
        ILocator locator,
        string attribute,
        CancellationToken cancellationToken)
    {
        try
        {
            return await locator.GetAttributeAsync(
                    attribute,
                    new LocatorGetAttributeOptions { Timeout = (float)LocatorProbeTimeout.TotalMilliseconds })
                .WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static ProviderAccountFieldEvidence Field(
        ProviderAccountObservationField field,
        string? value,
        string? failureReasonCode,
        ProviderAccountObservationFieldStatus status,
        string source,
        string evidenceType)
        => new(
            field,
            string.IsNullOrWhiteSpace(value) ? null : value.Trim(),
            source,
            evidenceType,
            status == ProviderAccountObservationFieldStatus.Observed ? "High" : "Low",
            status,
            DateTimeOffset.UtcNow,
            failureReasonCode);

    private static string? ExtractPlanLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!Matches(line, "current plan|plan|subscription|account status|trạng thái tài khoản|gói|thuê bao|abonnement"))
            {
                continue;
            }

            var candidates = IsPromotionalPlanText(line)
                ? Array.Empty<string>()
                : new[] { line };
            if (index + 1 < lines.Length)
            {
                candidates = candidates.Append(lines[index + 1]).ToArray();
            }

            foreach (var candidate in candidates)
            {
                if (Matches(candidate, "enterprise|doanh nghiệp")) return "Enterprise";
                if (Matches(candidate, "business|doanh nghi[eệ]p")) return "Business";
                if (Matches(candidate, "\\bplus\\b")) return "Plus";
                if (Matches(candidate, "\\bpro\\b")) return "Pro";
                if (Matches(candidate, "\\bgo\\b")) return "Go";
                if (Matches(candidate, "\\bfree\\b|miễn phí")) return "Free";
            }
        }

        return null;
    }

    private static string? ExtractMfaLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var relevantLines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => Matches(line, "mfa|2fa|two-factor|multi-factor|authenticator|xác thực hai bước|xác thực hai yếu tố|bảo mật"))
            .ToArray();
        if (relevantLines.Length == 0)
        {
            return null;
        }

        var statusText = string.Join(" ", relevantLines);
        if (Matches(statusText, "\\bdisabled\\b|\\boff\\b|\\binactive\\b|not enabled|not active|chưa bật|tắt|không hoạt động")) return "Disabled";
        if (Matches(statusText, "\\benabled\\b|\\bon\\b|\\bactive\\b|đã bật|bật|đang hoạt động")) return "Enabled";
        return "Unknown";
    }

    private static bool IsPromotionalPlanText(string line)
        => Matches(line, "upgrade|upgrade to|get pro|get plus|try pro|try plus|compare plans|see plans|bắt đầu|nâng cấp|so sánh gói");

    private bool IsOfficialChatGptSettingsRoute(Uri? uri)
        => uri is not null
            && _isAllowedOrigin(uri)
            && uri.AbsolutePath.StartsWith("/settings/", StringComparison.OrdinalIgnoreCase);

    private bool IsOfficialChatGptAccountSettingsPage(Uri? uri)
        => uri is not null
            && _isAllowedOrigin(uri)
            && uri.AbsolutePath.TrimEnd('/').Equals("/settings/account", StringComparison.OrdinalIgnoreCase);

    private static string DetectLocale(string? menuText, string settingsText)
        => Matches((menuText ?? string.Empty) + " " + settingsText, "[ăâđêôơưáàảãạéèẻẽẹíìỉĩịóòỏõọúùủũụýỳỷỹỵ]|cài đặt|tài khoản")
            ? "vi"
            : "en";

    private async Task<BodyObservationResult> ReadBodyTextWithRetryAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(BodyObservationBudget);
        var lastFailureCode = "BROWSER_DOM_READ_TIMEOUT";
        var lastFailureMessage = "Trang provider chưa ổn định để đọc DOM trong thời gian quan sát cho phép.";

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                var timeout = (int)Math.Max(
                    100,
                    Math.Min(BodyReadAttemptTimeout.TotalMilliseconds, remaining.TotalMilliseconds));

                // Reading the document directly avoids waiting for a transient body
                // locator while a provider SPA is replacing its root nodes.
                var documentText = await _page.EvaluateAsync<string?>(
                    "() => document.body?.innerText || document.body?.textContent || ''")
                    .WaitAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(documentText))
                {
                    return BodyObservationResult.Success(documentText);
                }

                var locatorText = await _page.Locator("body")
                    .InnerTextAsync(new LocatorInnerTextOptions { Timeout = timeout })
                    .WaitAsync(cancellationToken);
                return BodyObservationResult.Success(locatorText);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                lastFailureCode = "BROWSER_DOM_READ_TIMEOUT";
                lastFailureMessage = "Trang provider phản hồi DOM chậm; đang thử lại trong deadline quan sát.";
            }
            catch (PlaywrightException exception) when (IsClosedFailure(exception))
            {
                throw new BrowserAuthSessionException(
                    "BROWSER_CLOSED",
                    "Chrome đã đóng hoặc phiên trình duyệt không còn khả dụng.",
                    exception.GetType().Name);
            }
            catch (PlaywrightException exception)
            {
                lastFailureCode = IsNavigationFailure(exception)
                    ? "BROWSER_NAVIGATION_IN_PROGRESS"
                    : "BROWSER_DOM_READ_TRANSIENT";
                lastFailureMessage = IsNavigationFailure(exception)
                    ? "Provider đang chuyển trang; đang chờ trang ổn định trong deadline quan sát."
                    : "DOM provider đang thay đổi; đang thử lại trong deadline quan sát.";
            }

            var remainingDelay = deadline - DateTimeOffset.UtcNow;
            if (remainingDelay <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remainingDelay < RetryDelay ? remainingDelay : RetryDelay,
                cancellationToken);
        }

        return BodyObservationResult.Failure(lastFailureCode, lastFailureMessage);
    }

    private static bool IsClosedFailure(PlaywrightException exception)
        => exception.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Target page", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("browser has been closed", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("context has been closed", StringComparison.OrdinalIgnoreCase);

    private static bool IsNavigationFailure(PlaywrightException exception)
        => exception.Message.Contains("navigation", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("frame was detached", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("execution context was destroyed", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientObservationFailure(PlaywrightException exception)
        => IsNavigationFailure(exception)
            || exception.Message.Contains("waiting for Locator", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not attached", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("element is not stable", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);

    private sealed record BodyObservationResult(
        bool IsSuccess,
        string Text,
        string? FailureCode,
        string? FailureMessage)
    {
        public static BodyObservationResult Success(string text)
            => new(true, text, null, null);

        public static BodyObservationResult Failure(string code, string message)
            => new(false, string.Empty, code, message);
    }

    private static string? ExtractEmail(string? value)
        => ExtractEmailEvidence(value).Email;

    private static EmailExtraction ExtractEmailEvidence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new(null, "IDENTITY_EMAIL_NOT_EXPOSED");
        }

        var matches = Regex.Matches(
            value,
            @"(?<![A-Za-z0-9._%+\-])[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}(?![A-Za-z0-9._%+\-])",
            RegexOptions.CultureInvariant);
        var candidates = matches
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.Length switch
        {
            1 => new(candidates[0], null),
            > 1 => new(null, "IDENTITY_EMAIL_AMBIGUOUS"),
            _ => new(null, "IDENTITY_EMAIL_NOT_EXPOSED")
        };
    }

    private sealed record EmailExtraction(string? Email, string? FailureCode);

    private async Task<ILocator> FindCredentialScopeAsync(
        ILocator field,
        CancellationToken cancellationToken)
    {
        var form = field.Locator("xpath=ancestor::form[1]");
        if (await form.CountAsync().WaitAsync(cancellationToken) > 0
            && await form.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            return form;
        }

        var dialog = field.Locator("xpath=ancestor::*[@role='dialog' or @aria-modal='true'][1]");
        if (await dialog.CountAsync().WaitAsync(cancellationToken) > 0
            && await dialog.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            return dialog;
        }

        return _page.Locator("body");
    }

    private async Task<bool> IsModalScopeAsync(
        ILocator emailField,
        CancellationToken cancellationToken)
    {
        var dialog = emailField.Locator("xpath=ancestor::*[@role='dialog' or @aria-modal='true'][1]");
        if (await dialog.CountAsync().WaitAsync(cancellationToken) > 0
            && await dialog.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            return true;
        }

        var modalClass = emailField.Locator(
            "xpath=ancestor::*[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'modal')][1]");
        return await modalClass.CountAsync().WaitAsync(cancellationToken) > 0
            && await modalClass.IsVisibleAsync().WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ILocator>> FindVisibleContinueActionsAsync(
        ILocator scope,
        string[] continueNames,
        CancellationToken cancellationToken)
    {
        // Prefer exact accessible button names. This keeps Google, Apple, phone,
        // and other provider actions out of the email continuation path.
        var exactMatches = new List<ILocator>();
        foreach (var name in continueNames)
        {
            var locator = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name, Exact = true });
            var count = await locator.CountAsync().WaitAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                var candidate = locator.Nth(index);
                if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    exactMatches.Add(candidate);
                }
            }
        }

        if (exactMatches.Count > 0)
        {
            return exactMatches;
        }

        var semanticMatches = new List<ILocator>();
        var semanticButtons = scope.Locator("button, input[type='submit']");
        var semanticCount = await semanticButtons.CountAsync().WaitAsync(cancellationToken);
        for (var index = 0; index < semanticCount; index++)
        {
            var candidate = semanticButtons.Nth(index);
            if (!await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
            {
                continue;
            }

            var label = await GetAccessibleLabelAsync(candidate, cancellationToken);
            if (IsContinueLabel(label, continueNames))
            {
                semanticMatches.Add(candidate);
            }
        }

        if (semanticMatches.Count > 0)
        {
            return semanticMatches;
        }

        var dataTestMatches = scope.Locator(
            "[data-testid*='continue' i], [data-testid*='submit' i], [data-testid*='login-submit' i]");
        var dataTestCount = await dataTestMatches.CountAsync().WaitAsync(cancellationToken);
        var visibleDataTestMatches = new List<ILocator>();
        for (var index = 0; index < dataTestCount; index++)
        {
            var candidate = dataTestMatches.Nth(index);
            if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
            {
                var label = await GetAccessibleLabelAsync(candidate, cancellationToken);
                if (string.IsNullOrWhiteSpace(label)
                    || IsContinueLabel(label, continueNames))
                {
                    visibleDataTestMatches.Add(candidate);
                }
            }
        }

        if (visibleDataTestMatches.Count > 0)
        {
            return visibleDataTestMatches;
        }

        var submitButtons = scope.Locator("button[type='submit'], input[type='submit'], button[data-action-button-primary='true']");
        var submitCount = await submitButtons.CountAsync().WaitAsync(cancellationToken);
        var visibleSubmits = new List<ILocator>();
        for (var index = 0; index < submitCount; index++)
        {
            var candidate = submitButtons.Nth(index);
            if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
            {
                visibleSubmits.Add(candidate);
            }
        }

        if (visibleSubmits.Count == 1)
        {
            return visibleSubmits;
        }

        return visibleDataTestMatches;
    }

    private static async Task<string> GetAccessibleLabelAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        var ariaLabel = await ReadLocatorAttributeAsync(locator, "aria-label", cancellationToken);
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return ariaLabel.Trim();
        }

        var text = await ReadLocatorTextAsync(locator, cancellationToken);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return (await ReadLocatorAttributeAsync(locator, "value", cancellationToken) ?? string.Empty).Trim();
    }

    private static bool IsContinueLabel(string label, string[] continueNames)
    {
        var normalized = Regex.Replace(label.Trim(), "\\s+", " ");
        if (EmailAlternativeTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return continueNames.Any(name => normalized.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<ILocator>> FindVisibleLoginEntriesAsync(CancellationToken cancellationToken)
    {
        // ChatGPT renders several guest buttons for the same modal action. Treat the
        // provider's explicit login action as one logical CTA, while keeping the
        // generic fallback strict for pages without that semantic marker.
        var canonicalEntries = _page.Locator("[data-mobile-auth-entry-action='login']");
        var canonicalCount = await canonicalEntries.CountAsync().WaitAsync(cancellationToken);
        for (var index = 0; index < canonicalCount; index++)
        {
            var candidate = canonicalEntries.Nth(index);
            try
            {
                if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    return [candidate];
                }
            }
            catch (PlaywrightException)
            {
                // A transient DOM change is not treated as a valid login entry.
            }
        }

        var matches = new List<ILocator>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in new[] { AriaRole.Button, AriaRole.Link })
        {
            foreach (var name in LoginEntryNames)
            {
                var locator = _page.GetByRole(role, new PageGetByRoleOptions { Name = name, Exact = true });
                var count = await locator.CountAsync().WaitAsync(cancellationToken);
                for (var index = 0; index < count; index++)
                {
                    var candidate = locator.Nth(index);
                    try
                    {
                        if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
                        {
                            await AddDistinctAsync(candidate);
                        }
                    }
                    catch (PlaywrightException)
                    {
                        // A transient DOM change is not treated as a valid login entry.
                    }
                }

                if (count == 0)
                {
                    var semanticElements = _page.Locator(role == AriaRole.Button ? "button" : "a");
                    var semanticCount = await semanticElements.CountAsync().WaitAsync(cancellationToken);
                    for (var index = 0; index < semanticCount; index++)
                    {
                        var candidate = semanticElements.Nth(index);
                        try
                        {
                            if (!await candidate.IsVisibleAsync().WaitAsync(cancellationToken))
                            {
                                continue;
                            }

                            var text = (await candidate.InnerTextAsync(
                                    new LocatorInnerTextOptions { Timeout = (float)LocatorProbeTimeout.TotalMilliseconds })
                                .WaitAsync(cancellationToken)).Trim();
                            if (text.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                await AddDistinctAsync(candidate);
                            }
                        }
                        catch (TimeoutException)
                        {
                            // The provider replaced this candidate while the page was transitioning.
                        }
                        catch (PlaywrightException)
                        {
                            // A transient DOM change is not treated as a valid login entry.
                        }
                    }
                }
            }
        }

        return matches;

        async Task AddDistinctAsync(ILocator candidate)
        {
            var signature = string.Join(
                "|",
                await ReadLocatorAttributeAsync(candidate, "href", cancellationToken) ?? string.Empty,
                await ReadLocatorAttributeAsync(candidate, "command", cancellationToken) ?? string.Empty,
                await ReadLocatorAttributeAsync(candidate, "commandfor", cancellationToken) ?? string.Empty,
                (await ReadLocatorTextAsync(candidate, cancellationToken) ?? string.Empty).Trim());
            if (signatures.Add(signature))
            {
                matches.Add(candidate);
            }
        }
    }

    private async Task<bool> IsPageReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = 3_000 }).WaitAsync(cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            // A readable body is enough for a bounded guest-page classification while late assets load.
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private async Task<ILocator?> WaitForFirstVisibleAsync(
        string[] selectors,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var found = await FindFirstVisibleAsync(selectors, cancellationToken);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }

        return await FindFirstVisibleAsync(selectors, cancellationToken);
    }

    private async Task<ILocator?> FindFirstVisibleAsync(string[] selectors, CancellationToken cancellationToken)
    {
        foreach (var selector in selectors)
        {
            var locator = _page.Locator(selector);
            int count;
            try
            {
                count = await locator.CountAsync().WaitAsync(cancellationToken);
            }
            catch (PlaywrightException)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                var candidate = locator.Nth(i);
                try
                {
                    if (await candidate.IsVisibleAsync().WaitAsync(cancellationToken)
                        && await candidate.IsEnabledAsync().WaitAsync(cancellationToken))
                    {
                        return candidate;
                    }
                }
                catch (PlaywrightException)
                {
                    // A transient locator failure is treated as not found and retried with the next candidate.
                }
            }
        }

        return null;
    }

    private static BrowserPageState DetectPageState(
        string bodyText,
        bool hasEmail,
        bool hasPassword,
        bool hasTotp,
        int loginEntryCount,
        bool pageReady)
    {
        if (Matches(bodyText, "captcha|verify you are human|are you human|xác minh bạn là con người|thử thách bảo mật"))
        {
            return BrowserPageState.Captcha;
        }

        if (Matches(bodyText, "rate[- ]limit|too many requests|too many attempts|try again later|temporarily blocked|quá nhiều yêu cầu|thử lại sau"))
        {
            return BrowserPageState.RateLimited;
        }

        if (Matches(bodyText, "wrong password|incorrect password|invalid password|your password was incorrect|password is incorrect|mật khẩu không chính xác|mật khẩu không đúng|sai mật khẩu|email or password is incorrect|tài khoản hoặc mật khẩu không đúng"))
        {
            return BrowserPageState.InvalidCredentials;
        }

        if (Matches(bodyText, "invalid authenticator code|invalid verification code|incorrect verification code|verification code is incorrect|invalid one-time code|mã xác thực không đúng|mã xác minh không đúng"))
        {
            return BrowserPageState.MfaRequired;
        }

        if (Matches(bodyText, "additional verification|additional authentication|another way|use another method|extra step|bước xác thực bổ sung|xác minh thêm|cần xác minh thêm|thử thách bổ sung|approve|approval|confirm it.s you|verify it.s you|verify your identity|security check|device confirmation|confirm this device|xác nhận thiết bị|phê duyệt|xác minh danh tính|xác minh thiết bị"))
        {
            return BrowserPageState.MfaRequired;
        }

        // Guest landing pages can expose a chat composer and "New chat" text.
        // Require an account-specific marker before reporting authentication.
        if (Matches(bodyText, "log out|sign out|account settings|manage account|my account|đăng xuất|cài đặt tài khoản|quản lý tài khoản|tài khoản của tôi"))
        {
            return BrowserPageState.Authenticated;
        }

        if (hasPassword)
        {
            return BrowserPageState.PasswordRequired;
        }

        if (hasTotp)
        {
            return BrowserPageState.MfaRequired;
        }

        // Provider alternatives can be rendered beside the email form. The
        // email path remains available when a stable email field is present.
        if (hasEmail)
        {
            return BrowserPageState.EmailRequired;
        }

        if (Matches(bodyText, "single sign-on|\\bsso\\b|continue with google|continue with microsoft|continue with apple|đăng nhập bằng google|tiếp tục với google|tiếp tục với microsoft|tiếp tục với apple"))
        {
            return BrowserPageState.ExternalProviderRequired;
        }

        if (Matches(bodyText, "passkey|security key|use your passkey|khóa bảo mật|sử dụng khóa bảo mật"))
        {
            return BrowserPageState.Passkey;
        }

        if (Matches(bodyText, "recovery code|backup code|mã khôi phục|mã dự phòng"))
        {
            return BrowserPageState.RecoveryCode;
        }

        if (Matches(bodyText, "sms code|text message|email code|verification code|check your email|mã sms|mã xác minh|kiểm tra email|mã gửi qua email|thiết bị mới|xác minh thiết bị"))
        {
            return BrowserPageState.SmsOrEmailCode;
        }

        if (pageReady && loginEntryCount == 1)
        {
            return BrowserPageState.GuestLandingLoginAvailable;
        }

        return BrowserPageState.Unknown;
    }

    private static MfaObservationKind ClassifyMfaObservation(
        string bodyText,
        BrowserPageState state,
        bool hasTotp,
        bool pageReady)
    {
        if (state == BrowserPageState.Authenticated)
        {
            return MfaObservationKind.AuthenticatedShellObserved;
        }

        if (state is BrowserPageState.Captcha
            or BrowserPageState.Passkey
            or BrowserPageState.RecoveryCode
            or BrowserPageState.SmsOrEmailCode
            or BrowserPageState.ExternalProviderRequired)
        {
            return MfaObservationKind.HumanConfirmationRequired;
        }

        if (Matches(bodyText, "invalid authenticator code|invalid verification code|incorrect verification code|verification code is incorrect|invalid one-time code|mã xác thực không đúng|mã xác minh không đúng"))
        {
            return MfaObservationKind.MfaValidationError;
        }

        if (Matches(bodyText, "approve|approval|confirm it.s you|verify it.s you|verify your identity|security check|device confirmation|confirm this device|xác nhận thiết bị|phê duyệt|xác minh danh tính|xác minh thiết bị"))
        {
            return MfaObservationKind.HumanConfirmationRequired;
        }

        if (Matches(bodyText, "additional verification|additional authentication|another way|use another method|extra step|bước xác thực bổ sung|xác minh thêm|cần xác minh thêm|thử thách bổ sung"))
        {
            return MfaObservationKind.AdditionalMfaChallenge;
        }

        if (Matches(bodyText, "checking|verifying|processing|loading|đang kiểm tra|đang xác minh|đang xử lý"))
        {
            return MfaObservationKind.MfaSubmissionProcessing;
        }

        if (!hasTotp)
        {
            return MfaObservationKind.Unknown;
        }

        return pageReady ? MfaObservationKind.MfaFormUnchanged : MfaObservationKind.Unknown;
    }

    private static bool Matches(string value, string pattern)
        => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _context.CloseAsync();
        }
        finally
        {
            _profileLease?.Dispose();
            _playwright.Dispose();
        }
    }
}
