# AUTH-BROWSER-04 — Real ChatGPT Login Routing Hotfix

Date: 2026-10-16  
Scope: Tầng 4 — Integrated User Mode browser routing only  
Verdict: **GO for routing; credential submission intentionally not exercised**

## Root Cause

The integrated browser session accepted a caller-supplied `officialUri` and the test composition also had a local `MockAuthenticationServer.LoginUri` (`http://127.0.0.1:<port>/login`). That allowed a mock destination to look like a valid browser-auth start when the wrong factory/composition was selected.

The production path now has a hard start boundary:

- `BrowserOriginPolicy.IsOfficialChatGptStart` accepts only `https://chatgpt.com/` (or the equivalent URI without the trailing slash), with no path, query, fragment, user info, or non-default port.
- `PlaywrightBrowserAuthenticationSessionFactory.OpenAsync` rejects mock URLs, `/login`, query redirects, and direct `auth.openai.com` starts before profile ownership or Chrome launch.
- `BrowserAuthOrchestrator.StartAsync` always passes `BrowserOriginPolicy.ChatGptUri` to the production session factory.
- Test-only local URLs remain inside `LocalPlaywrightSessionFactory` and `MockAuthenticationServer`; the production DI registration never registers that factory.

`launchSettings.json` only controls the FLASH ROUTER host endpoints (`http://localhost:5270` and `https://localhost:7146`). No production `BaseUrl`, `LoginUrl`, `AuthUrl`, `TestServer`, or `MockAuth` configuration was found that should control Integrated User Mode. `FlashAI:Browser:ChromeExecutable` selects the Chrome executable only. The `auth.openai.com` constants belong to the separate official OAuth client and are not used as the Integrated User Mode start URL.

## Call-Path Trace

1. `BrowserAuthDialog.razor` presents the controlled `Hỗ trợ đăng nhập ChatGPT` dialog and separates the guest CTA, email, password, and TOTP confirmation actions.
2. `MainWorkspace.razor:837-846` handles `Hỗ trợ đăng nhập`, opens the dialog, and calls `IBrowserAuthOrchestrator.StartAsync(accountId)`.
3. `BrowserAuthOrchestrator.cs:50-76` resolves the selected account email, asks `IBrowserProfileManager` to prepare the account-owned managed profile, then calls the registered `IBrowserAuthenticationSessionFactory` with `BrowserOriginPolicy.ChatGptUri`.
4. `BrowserProfileManager.cs:61-104` resolves Chrome, verifies the account, creates/reuses only the managed profile binding under the configured workspace root, and returns the session context. It does not choose a login URL.
5. `PlaywrightBrowserAuthenticationSessionFactory.cs:25-77` validates the exact official start, acquires profile ownership, launches headed Chrome with `ChromiumSandbox = true`, and navigates to `https://chatgpt.com/`.
6. `PlaywrightBrowserAuthenticationSession.cs:242-273` revalidates the current origin after navigation. `ActivateLoginEntryAsync` revalidates before and after the CTA action; `FillAsync` revalidates immediately before any sensitive field fill.

The separate direct browser-opening action also uses the centralized URL: `BrowserProfileUiPort.cs:9` derives its target from `BrowserOriginPolicy.ChatGptUri`; it does not hard-code `/login`.

## Source Changes

### Production routing guard

`src/Modules/Connections/Application/BrowserAuthPageStateDetector.cs:7-33`

- Centralized `ChatGptUri` at `https://chatgpt.com/`.
- Added exact-start validation and a narrow official-origin allowlist for post-navigation checks.

`src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:25-30`

- Fail-closed `INTEGRATED_START_ORIGIN_REQUIRED` guard runs before executable/profile checks and before ownership acquisition.

### Official ChatGPT guest CTA

`src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:451-541`

ChatGPT currently renders several visible guest buttons with the same semantic action (`data-mobile-auth-entry-action="login"`). The session treats the first visible instance of that explicit provider action as one logical CTA. The generic role/text fallback remains strict and deduplicates equivalent candidates by action signature; distinct ambiguous generic candidates still fail closed.

This avoids hard-coding a provider `/login` route. The browser starts at the public landing page and activates the official page's own login action.

### DI and compatibility

`src/Modules/Connections/ConnectionsModuleRegistration.cs:65-87` keeps the production factory and profile services in DI and explicitly constructs `BrowserProfileUiPort` with `IBrowserProfileManager` and `IChromeProcessLauncher`. The existing compatibility constructor remains available. No Vault, SQLite schema, DPAPI keys, account records, or existing Chrome profiles were rebuilt or modified.

## Regression Coverage

`tests/FlashAI.IntegrationTests/AuthBrowser04RoutingTests.cs:8-54` verifies:

- An Integrated User Mode factory rejects `http://127.0.0.1:<port>/login` before ownership/browser launch.
- `https://chatgpt.com/` is accepted as the only official start.
- Direct `/login`, query redirect, and direct `https://auth.openai.com/` starts are rejected.

The existing headed local E2E suite continues to use its test-only `MockAuthenticationServer` and `LocalPlaywrightSessionFactory` (`BrowserAuthPlaywrightE2ETests.cs:356-464`), preserving Local Mock E2E without making its URL available to production DI.

## Verification

### Build and test counts

| Check | Result |
|---|---:|
| Debug integration tests | **138/138 PASS** |
| Release integration tests | **138/138 PASS** |
| Debug architecture tests | **8/8 PASS** |
| Release architecture tests | **8/8 PASS** |
| Debug solution build | **PASS, 0 errors** |
| Release solution build | **PASS, 0 errors** |

### Real host smoke evidence

The host was started twice with separate disposable roots under `%TEMP%`, with Integrated storage enabled. No existing application data root was used.

HTTP launch profile (`FLASH ROUTER/Properties/launchSettings.json:12-20`):

- Endpoint: `http://localhost:5270`
- `/health/ready`: `200`
- `/`: `200`
- `/_framework/blazor.web.js`: `200`
- `foundation/tokens.css`, `app.css`, `FLASH_ROUTER.styles.css`: `200`
- All 11 module CSS bundles discovered from the rendered page: `200`
- Host stopped cleanly; no `5270`/`7146` listener remained; managed runtime secret/instance files were removed.

HTTPS launch profile (`FLASH ROUTER/Properties/launchSettings.json:21-28`):

- Endpoint: `https://localhost:7146` (plus `http://localhost:5270`)
- `/health/ready`: `200`
- `/`: `200`
- `/_framework/blazor.web.js`: `200`
- `app.css` and all 11 module CSS bundles: `200`
- Development certificate was accepted only by the local probe with certificate checking disabled; no external certificate or provider operation was performed.
- Host stopped cleanly and listener/runtime cleanup matched the HTTP run.

### Headed Chrome production probe

The probe invoked the real `PlaywrightBrowserAuthenticationSessionFactory` with a newly created disposable Chrome profile, installed Chrome executable, headed mode, `ChromiumSandbox = true`, and no credentials.

Before the CTA fix, the real page exposed multiple login buttons and the strict detector returned `Unknown`; this was a genuine guest-page detection gap, not a localhost routing success.

After the fix:

```text
LIVE_START=https://chatgpt.com/
LIVE_LANDING_STATE=GuestLandingLoginAvailable
LIVE_LOGIN_ENTRY_COUNT=1
LIVE_AFTER_CTA=https://chatgpt.com/
LIVE_AFTER_CTA_STATE=EmailRequired
LIVE_AFTER_CTA_EMAIL_FIELD=True
LIVE_AFTER_CTA_PASSWORD_FIELD=False
```

The probe never entered an email, password, TOTP, cookie, token, or query string. The final origin remained `chatgpt.com`; no `localhost` or `127.0.0.1` URL was opened by the production factory. The disposable profile was closed and deleted after the probe; no live probe profile directory remained.

## Safety and Non-Goals

- No supplied test account was read or used.
- No real ChatGPT login was submitted and no provider API was called.
- No Vault, SQLite, DPAPI key, existing account, or existing Chrome profile was deleted or migrated.
- No GitHub, installer, PATH, or Tầng 6 work was performed.
- A successful browser launch is recorded as browser/session evidence only; it is not treated as OAuth authorization, verified identity, or credential success.

## Acceptance

**GO for AUTH-BROWSER-04 routing:** the FLASH ROUTER support-login callback routes through the production orchestrator to headed Chrome at `https://chatgpt.com/`, recognizes the official guest login action, and reaches the official email form without exposing a mock login URL. Local mock E2E remains test-only and is rejected by the Integrated User Mode factory.
