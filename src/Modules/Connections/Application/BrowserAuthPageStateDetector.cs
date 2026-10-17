using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public static class BrowserOriginPolicy
{
    public static Uri ChatGptUri { get; } = new("https://chatgpt.com/", UriKind.Absolute);

    // Integrated User Mode must always begin at the public ChatGPT landing page.
    // The broader allowlist is reserved for trusted redirects after navigation.
    public static bool IsOfficialChatGptStart(Uri? uri)
        => uri is not null
            && uri.IsAbsoluteUri
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals(ChatGptUri.Host, StringComparison.OrdinalIgnoreCase)
            && (uri.Port is -1 or 443)
            && uri.AbsolutePath is "/" or ""
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsAllowed(Uri? uri)
        => uri is not null
            && uri.IsAbsoluteUri
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && AllowedHosts.Contains(uri.Host);

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "chatgpt.com",
        "chat.openai.com",
        "auth.openai.com"
    };
}

public sealed class StableBrowserPageStateDetector : IBrowserPageStateDetector
{
    private readonly Func<Uri?, bool> _isAllowedOrigin;

    public StableBrowserPageStateDetector(Func<Uri?, bool>? isAllowedOrigin = null)
    {
        _isAllowedOrigin = isAllowedOrigin ?? BrowserOriginPolicy.IsAllowed;
    }

    public BrowserPageState Detect(BrowserPageSnapshot snapshot)
    {
        if (!_isAllowedOrigin(snapshot.CurrentUri))
        {
            return BrowserPageState.Unknown;
        }

        if (snapshot.State is BrowserPageState.Authenticated
            or BrowserPageState.Captcha
            or BrowserPageState.RateLimited
            or BrowserPageState.Passkey
            or BrowserPageState.RecoveryCode
            or BrowserPageState.SmsOrEmailCode)
        {
            return snapshot.State;
        }

        if (snapshot.HasPasswordField)
        {
            return BrowserPageState.PasswordRequired;
        }

        if (snapshot.HasTotpField)
        {
            return BrowserPageState.MfaRequired;
        }

        if (snapshot.HasEmailField)
        {
            return BrowserPageState.EmailRequired;
        }

        if (snapshot.State == BrowserPageState.ExternalProviderRequired)
        {
            return BrowserPageState.ExternalProviderRequired;
        }

        if (snapshot.IsPageReady
            && snapshot.HasGuestLoginEntry
            && snapshot.GuestLoginEntryCount == 1)
        {
            return BrowserPageState.GuestLandingLoginAvailable;
        }

        return snapshot.State switch
        {
            BrowserPageState.GuestLandingLoginAvailable when snapshot.GuestLoginEntryCount == 1
                => BrowserPageState.GuestLandingLoginAvailable,
            BrowserPageState.GuestLandingLoginAvailable => BrowserPageState.Unknown,
            _ => snapshot.State
        };
    }
}
