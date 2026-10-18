using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.IntegrationTests;

public sealed class Console04UiRebuildTests
{
    private static string WorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Workspace root not found.");
    }

    [Fact]
    public void TokensCssDefinesMandatoryDarkDefaultAndLightTokens()
    {
        var root = WorkspaceRoot();
        var tokensCss = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "wwwroot", "foundation", "tokens.css"));

        // Mandatory Dark tokens (Default)
        Assert.Contains("--app-bg: #081320", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-surface: #101E30", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-elevated: #14263A", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-input: #0D1D30", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-console: #07121E", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-border: #25364A", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--text-primary: #E7EEF8", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--text-muted: #95A9C3", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--accent-primary: #2681FF", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--accent-teal: #16C5AB", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--state-success: #2DDD9B", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--state-warning: #F5B94E", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--state-error: #F97785", tokensCss, StringComparison.OrdinalIgnoreCase);

        // Mandatory Light tokens
        Assert.Contains("--app-bg: #EFF3F9", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-surface: #FFFFFF", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-elevated: #F5F8FC", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-input: #FFFFFF", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-console: #102033", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--app-border: #D5DEEB", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--text-primary: #192B40", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--text-muted: #5A6F88", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--accent-primary: #176CE3", tokensCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--accent-teal: #078F7E", tokensCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppRazorContainsPrePaintThemeScriptToPreventFlash()
    {
        var root = WorkspaceRoot();
        var appRazor = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "App.razor"));

        Assert.Contains("localStorage.getItem('flashrouter_theme')", appRazor, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.setAttribute('data-theme'", appRazor, StringComparison.Ordinal);
        Assert.DoesNotContain("/legacy/accounts", appRazor, StringComparison.Ordinal);
        Assert.DoesNotContain("/accounts", appRazor, StringComparison.Ordinal);
    }

    [Fact]
    public void AppJsContainsSafeThemeManagementWithoutSecretExposure()
    {
        var root = WorkspaceRoot();
        var appJs = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "wwwroot", "app.js"));

        Assert.Contains("getTheme:", appJs, StringComparison.Ordinal);
        Assert.Contains("setTheme:", appJs, StringComparison.Ordinal);
        Assert.Contains("toggleTheme:", appJs, StringComparison.Ordinal);
        Assert.Contains("flashrouter_theme", appJs, StringComparison.Ordinal);

        // Verify storage only touches theme, never credentials or TOTP
        Assert.DoesNotContain("password", appJs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", appJs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totp", appJs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoLoginConsoleMarkupContainsDeveloperShellAndThemeToggle()
    {
        var root = WorkspaceRoot();
        var consoleRazor = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor"));

        // Header and Theme Toggle
        Assert.Contains("FLASH ROUTER", consoleRazor, StringComparison.Ordinal);
        Assert.Contains("AI Auto Login Console", consoleRazor, StringComparison.Ordinal);
        Assert.Contains("ToggleThemeAsync", consoleRazor, StringComparison.Ordinal);
        Assert.Contains("theme-toggle-btn", consoleRazor, StringComparison.Ordinal);

        // One-shot input line
        Assert.Contains("EMAIL|PASS|2FA", consoleRazor, StringComparison.Ordinal);
        Assert.DoesNotContain("Temporary", consoleRazor, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepBrowserSession", consoleRazor, StringComparison.Ordinal);

        // No legacy links
        Assert.DoesNotContain("/legacy/accounts", consoleRazor, StringComparison.Ordinal);
        Assert.DoesNotContain("/accounts", consoleRazor, StringComparison.Ordinal);
        Assert.DoesNotContain("Account Manager", consoleRazor, StringComparison.Ordinal);

        // No Georgia/serif styles in Razor
        Assert.DoesNotContain("Georgia", consoleRazor, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoLoginConsoleCssUsesModernSansSerifAndNoSerifRules()
    {
        var root = WorkspaceRoot();
        var consoleCss = File.ReadAllText(Path.Combine(root, "FLASH ROUTER", "Components", "Console", "AutoLoginConsole.razor.css"));

        // Verify zero Georgia or serif usage
        Assert.DoesNotContain("Georgia", consoleCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serif", consoleCss, StringComparison.OrdinalIgnoreCase);

        // Verify responsive grid & token usage
        Assert.Contains("grid-template-columns", consoleCss, StringComparison.Ordinal);
        Assert.Contains("var(--app-bg)", consoleCss, StringComparison.Ordinal);
        Assert.Contains("var(--app-surface)", consoleCss, StringComparison.Ordinal);
        Assert.Contains("var(--app-console)", consoleCss, StringComparison.Ordinal);
        Assert.Contains("var(--accent-primary)", consoleCss, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickInputEscapedSeparatorsHandledSafely()
    {
        // Special characters and escaped pipe and backslash in password
        const string input = "developer@flashrouter.local|P@$$w0rd\\\\with\\\\slash\\|WithPipes#123|JBSWY3DPEHPK3PXP";
        Assert.True(CredentialLineCodec.TryParse(input, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal("developer@flashrouter.local", parsed.Account);
        Assert.Equal("P@$$w0rd\\with\\slash|WithPipes#123", parsed.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", parsed.TotpSeed);

        // Ensure error message (if any) never echoes secret
        Assert.DoesNotContain("P@$$w0rd", error ?? string.Empty, StringComparison.Ordinal);
    }
}
