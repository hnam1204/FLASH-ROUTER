# AUTH-BROWSER-06 - Real ChatGPT Login Acceptance

Date: 2026-10-17  
Scope: Tier 4 real ChatGPT browser acceptance  
Verdict: **WAITING FOR USER - public routing PASS; authenticated login BLOCKED**

## Executive result

The FLASH ROUTER route now starts Integrated User Mode at the official public landing page, not a local mock login route. Headed Chrome production evidence reached the official guest landing, detected the official login CTA, and reached the official email form without using credentials.

The authenticated portion is intentionally not accepted. The user pasted a credential string into chat, but did not provide the required confirmation that the password and TOTP secret were rotated, the Vault entry was updated, and the live action was explicitly approved inside FLASH ROUTER. The pasted values were not read, copied, stored, tested, or used.

## Call-path trace

1. `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor:837-846` handles `Hỗ trợ đăng nhập` and calls `IBrowserAuthOrchestrator.StartAsync` for the selected account.
2. `src/Modules/Connections/Application/BrowserAuthOrchestrator.cs:48-81` resolves the selected account email, prepares the account-owned managed profile through `IBrowserProfileManager`, and passes only `BrowserOriginPolicy.ChatGptUri` to the session factory.
3. `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:20-77` rejects every non-official start before profile ownership or browser launch, starts headed Chrome with `ChromiumSandbox = true`, and navigates to `https://chatgpt.com/`.
4. `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:341-420` identifies and activates the official login CTA with origin checks before and after the action.
5. `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:480-540` scopes the email Continue action to the email form, rejects missing or ambiguous actions, and revalidates the official origin after the click.
6. `src/Modules/Connections/Application/BrowserProfileUiPort.cs:9-27` also derives the direct ChatGPT browser destination from the centralized official URI; it does not use `/login`.

## Configuration and mode audit

- `BrowserOriginPolicy.ChatGptUri` is the single integrated start URI: `https://chatgpt.com/`.
- `PlaywrightBrowserAuthenticationSessionFactory` rejects `127.0.0.1`, `localhost`, `/login`, query redirects, and direct `auth.openai.com` starts with `INTEGRATED_START_ORIGIN_REQUIRED`.
- `MockAuthenticationServer` and `LocalPlaywrightSessionFactory` exist only in the integration-test project and are not registered by production DI.
- No production `BaseUrl`, `LoginUrl`, `AuthUrl`, `TestServer`, or `MockAuth` override selects the Integrated User Mode destination.
- `FLASH ROUTER/Properties/launchSettings.json` controls only local host endpoints. It does not control the provider destination.
- `src/Modules/Connections/ConnectionsModuleRegistration.cs:78-87` explicitly constructs `BrowserProfileUiPort` with `IBrowserProfileManager` and `IChromeProcessLauncher`, preventing the earlier constructor ambiguity.

## Mode separation

| Mode | Destination | Credential policy | Result |
|---|---|---|---|
| Local Mock E2E | `http://127.0.0.1:<test-port>/` or `/login` | Synthetic values only | PASS; test-only factory |
| Integrated User Mode | `https://chatgpt.com/` then the site's own CTA | No automatic use without Vault and in-app confirmation | PASS through email form; live credential step waiting |

The integrated factory cannot select the mock destination even if a caller supplies it. Regression coverage is in `tests/FlashAI.IntegrationTests/AuthBrowser04RoutingTests.cs:9-54`.

## Verification results

| Check | Result |
|---|---:|
| Debug solution build | **PASS, 0 warnings, 0 errors** |
| Release solution build | **PASS, 0 warnings, 0 errors** |
| Debug architecture tests | **8/8 PASS** |
| Debug integration tests | **141/141 PASS** |
| Release architecture tests | **8/8 PASS** |
| Release integration tests | **141/141 PASS** |
| Full host DI validation and browser service resolution | **PASS** |
| Synthetic headed browser lifecycle and sandbox coverage | **PASS** |
| Production headed public navigation probe | **PASS, no credentials** |
| Password/TOTP submission | **NOT EXECUTED** |
| Authenticated session and persistence acceptance | **BLOCKED / WAITING FOR USER** |

Validation note: one initial Release run had a transient headed-browser state mismatch in the 60-second guest-session test (`EmailRequired` was observed where the guest landing was expected). The same test passed in isolation (`1/1`), and the immediately repeated full Release run passed `141/141`; no production source change was made for that transient.

## Runtime and browser evidence

The recorded disposable-host smoke evidence covered both Visual Studio launch profiles:

- `http`: `http://localhost:5270`, health readiness `200`, `/` `200`, Blazor framework asset `200`, app CSS and all discovered module CSS bundles `200`.
- `https`: `https://localhost:7146` plus HTTP `5270`, health readiness and page/assets `200`; the local development certificate was accepted only by the local probe.
- The production headed probe used the real `PlaywrightBrowserAuthenticationSessionFactory`, a disposable account-owned profile, installed Chrome, and `ChromiumSandbox = true`.
- Observed public flow: `https://chatgpt.com/` -> `GuestLandingLoginAvailable` -> official login CTA -> `EmailRequired`.
- No `localhost` or `127.0.0.1` destination was opened by the production factory.
- No password, TOTP, cookie, token, query-bearing URL, provider API, or existing Chrome profile was accessed.

The current workspace has no listener on ports `5270` or `7146`. Pre-existing `.local/runtime/control.secret` and `instance.json` metadata point to a dead host PID and were left untouched because they belong to the existing user data root; no account, Vault, SQLite, DPAPI, or Chrome data was deleted.

## Final acceptance matrix

| Acceptance item | Verdict |
|---|---|
| FLASH ROUTER support-login action opens official ChatGPT | **PASS** |
| Official guest login CTA is recognized | **PASS** |
| Official email form is reached without hard-coded `/login` | **PASS** |
| Mock login URL is unreachable from Integrated User Mode | **PASS** |
| Password/TOTP are sent only after origin, Vault, account, and confirmation gates | **IMPLEMENTED; NOT EXERCISED** |
| Real account authenticated session is observed | **WAITING FOR USER** |
| Authenticated session persists after close/reopen | **WAITING FOR USER** |

## Required next gate

Before a future live attempt, rotate the exposed account password and TOTP secret, update the selected account's Vault record locally, verify the masked account/profile summary in FLASH ROUTER, and approve each requested action in the application. Do not paste replacement secrets into Codex, source files, reports, terminals, screenshots, or tests.

