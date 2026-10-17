# AUTH-BROWSER-07 Real Account Login Certification

Date: 2026-10-17
Scope: Tier 4 browser-authentication implementation and certification only

## Verdict

- `AUTH-BROWSER-07 OFFLINE: GO`
- `AUTH-BROWSER-07 LIVE: WAITING FOR USER`
- `TIER 4 REAL LOGIN: WAITING FOR USER`

The production DI graph, browser-auth state machine, mock/integrated routing separation, disposable-host startup, and public ChatGPT routing are certified. Real password entry, TOTP entry, authenticated-session observation, and close/reopen persistence were not executed.

## Root cause and minimal fix

`BrowserProfileUiPort` retained two public constructors after AUTH-BROWSER-01: the legacy four-dependency constructor and the canonical `IBrowserProfileManager` plus `IChromeProcessLauncher` constructor. `ConnectionsModuleRegistration` used implicit `AddScoped<BrowserProfileUiPort>()`, so both constructors were resolvable and the default Microsoft DI activator reported constructor ambiguity during `builder.Build()`.

The minimal compatibility-preserving fix is an explicit DI factory in `src/Modules/Connections/ConnectionsModuleRegistration.cs:78-87`. It selects the canonical constructor while retaining the legacy public constructor in `src/Modules/Connections/Application/BrowserProfileUiPort.cs:14-28` for existing direct callers. No BrowserProfileManager rebuild, Vault change, SQLite schema/data change, DPAPI key change, or existing Chrome profile change was made.

## Routing and safety trace

The integrated path is:

`BrowserAuthDialog` -> `MainWorkspace.HandleAssistLogin` -> `BrowserAuthOrchestrator.StartAsync` -> `IBrowserProfileManager.PrepareAsync` -> `PlaywrightBrowserAuthenticationSessionFactory.OpenAsync` -> headed Chrome.

- The UI entry is `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor:8`; `MainWorkspace.HandleAssistLogin` starts the orchestrator at `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor:837-845`.
- `BrowserProfileUiPort` starts ordinary managed browser usage at `https://chatgpt.com/` (`src/Modules/Connections/Application/BrowserProfileUiPort.cs:9,58-79`).
- `PlaywrightBrowserAuthenticationSessionFactory` rejects every non-official start before profile ownership or browser launch (`src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:20-51`). Localhost, `127.0.0.1`, direct `/login`, query redirects, and direct `auth.openai.com` starts are rejected with `INTEGRATED_START_ORIGIN_REQUIRED`.
- The production factory is the only authentication-session factory registered in `src/Modules/Connections/ConnectionsModuleRegistration.cs:68-71`.
- The adapter launches headed Chrome with `ChromiumSandbox = true` and an account-managed persistent profile (`src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:61-76`).
- Login CTA, email, password, and TOTP actions re-check the trusted origin. Password and TOTP reads remain Vault-gated, require distinct in-app confirmation, and activate one scoped Continue/Verify action only (`src/Modules/Connections/Application/BrowserAuthOrchestrator.cs:215-317` and `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:507-600`).
- Guest `New chat` text is not treated as authenticated. Authenticated observation requires account-specific markers such as sign-out or account settings.
- Mock E2E composition may use `127.0.0.1:<test-port>/login` only through its local test factory. Integrated production DI has no mock destination override.

## Regression and composition evidence

`tests/FlashAI.IntegrationTests/FlashRouterHostCompositionTests.cs:24-90` builds the complete host composition with `ValidateOnBuild = true` and `ValidateScopes = true`, then resolves `IBrowserProfileUiPort`, `IBrowserAuthOrchestrator`, `IBrowserProfileManager`, and `IChromeProcessLauncher`. The resolved integrated port is `BrowserProfileUiPort` and the orchestrator is `BrowserAuthOrchestrator`.

The routing regression suite also verifies that an integrated factory rejects `http://127.0.0.1:<port>/login` before ownership or browser launch and that only the public ChatGPT landing page is accepted (`tests/FlashAI.IntegrationTests/AuthBrowser04RoutingTests.cs`).

## Validation results

| Check | Result |
|---|---:|
| Debug solution build | PASS, 0 errors, 35 existing analyzer warnings |
| Release solution build | PASS, 0 errors, 35 existing analyzer warnings |
| Debug architecture tests | PASS, 8/8 |
| Release architecture tests | PASS, 8/8 |
| Debug integration tests | PASS, 142/142, 0 skipped |
| Release integration tests | PASS, 142/142, 0 skipped |
| DI hotfix host-composition regression | PASS, included in 142 integration tests |
| Integrated local/mock routing regression | PASS |
| Chromium sandbox assertion in headed fixture | PASS |
| Browser ownership and disposable profile lifecycle | PASS in headed synthetic fixture |

## Runtime evidence

The host was started twice with a disposable `FlashAI__WorkspaceRoot` and the Visual Studio launch profiles:

- `http`: `http://localhost:5270`
- `https`: `https://localhost:7146` plus `http://localhost:5270`

Observed results:

- HTTP `/health/ready`: `200`, `{"status":"ready","version":"1.0.0"}`.
- HTTP `/`: `200`, FLASH ROUTER markup present.
- HTTP `/_framework/blazor.web.js`: `200`.
- HTTP Connections CSS bundle: `200`, `_content/FlashAI.Modules.Connections/FlashAI.Modules.Connections.b7c12p9jcv.bundle.scp.css`.
- HTTPS `/health/ready`: `200`.
- HTTPS `/`: `200`, FLASH ROUTER markup present.
- HTTPS `/_framework/blazor.web.js`: `200`.
- HTTPS Connections CSS bundle: `200`.
- Integrated migration completed against the disposable SQLite root; no existing user database was selected.
- Host processes were stopped after the checks. No listener remained on ports `5270` or `7146`.

The disposable runtime root was isolated under the system temporary directory, was never connected to the user's existing `.local` data root, and was removed after the host stopped. No user data was deleted.

## Public ChatGPT probe

Credential-free public navigation reached `https://chatgpt.com/` and exposed the official `Đăng nhập` CTA. The prior headed Playwright production probe reached the same public landing page, activated the official CTA, and reached the official email form using a newly created disposable managed profile, installed Chrome, `Headless = false`, and `ChromiumSandbox = true`. No password, TOTP, cookie, token, provider API, or existing Chrome profile was used.

The current browser UI check was intentionally stopped before any credential field. An in-app browser limitation did not expose the subsequent form in that check; it is not counted as authenticated success. No mock URL appeared in the integrated route.

## Live-account gate

The account values previously pasted into chat are treated as compromised and were not read, copied, stored, tested, or mentioned in this report. A real attempt remains blocked until the user independently completes password rotation, TOTP reset/re-enrollment, local Vault update, and old-credential invalidation, then gives explicit in-app approval for one selected account and each sensitive action.

Until that gate is completed:

- no password or TOTP is submitted;
- no authenticated session is claimed;
- no session persistence is accepted;
- no provider API or private endpoint is called;
- no existing SQLite, Vault, DPAPI key ring, account record, or Chrome profile is modified.

## Final acceptance

The DI ambiguity is fixed, ASP.NET Core starts under both launch profiles, browser-auth services resolve, the FLASH ROUTER page and module assets are reachable, integrated mode starts from the official ChatGPT landing page, and local mock routing remains test-only. Offline/public routing is `GO`; real-account authentication and persistence remain `WAITING FOR USER` by design.
