using System.Text.RegularExpressions;
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

public sealed class PlaywrightBrowserAuthenticationSession : IBrowserAuthenticationSession, IBrowserAuthenticationSessionDiagnostics
{
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
            var pages = _context.Pages;
            if (pages.Count > 0 && !ReferenceEquals(pages[^1], _page))
            {
                var latest = pages[^1];
                if (Uri.TryCreate(latest.Url, UriKind.Absolute, out var latestUri) && _isAllowedOrigin(latestUri))
                {
                    _page = latest;
                }
            }

            var currentUri = CurrentUri;
            var bodyText = await _page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3_000 }).WaitAsync(cancellationToken);
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
                IsEmailModal: isEmailModal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException)
        {
            throw new BrowserAuthSessionException("BROWSER_CLOSED", "Chrome đã đóng hoặc phiên trình duyệt không còn khả dụng.");
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
        var ariaLabel = await locator.GetAttributeAsync("aria-label").WaitAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return ariaLabel.Trim();
        }

        var text = await locator.InnerTextAsync().WaitAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return (await locator.GetAttributeAsync("value").WaitAsync(cancellationToken) ?? string.Empty).Trim();
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

                            var text = (await candidate.InnerTextAsync().WaitAsync(cancellationToken)).Trim();
                            if (text.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                await AddDistinctAsync(candidate);
                            }
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
                await candidate.GetAttributeAsync("href").WaitAsync(cancellationToken) ?? string.Empty,
                await candidate.GetAttributeAsync("command").WaitAsync(cancellationToken) ?? string.Empty,
                await candidate.GetAttributeAsync("commandfor").WaitAsync(cancellationToken) ?? string.Empty,
                (await candidate.InnerTextAsync().WaitAsync(cancellationToken)).Trim());
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
