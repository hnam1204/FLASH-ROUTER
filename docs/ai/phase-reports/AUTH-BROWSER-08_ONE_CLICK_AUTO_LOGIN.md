# AUTH-BROWSER-08 One-Click Auto Login

Date: 2026-10-17  
Scope: Tier 4 browser-authentication implementation and certification only

## Verdict

- `AUTO LOGIN IMPLEMENTATION: GO`
- `AUTO LOGIN SYNTHETIC E2E: GO`
- `REAL CHATGPT CREDENTIAL ACCEPTANCE: WAITING FOR USER / BLOCKED`
- `REAL SESSION PERSISTENCE: WAITING FOR USER / BLOCKED`

The one-click workflow is implemented and runs as one bounded browser attempt. The real-account gates remain intentionally separate because the credential values previously pasted into chat were not used and must be treated as exposed.

## Root cause and prior limitation

Before AUTH-BROWSER-08, the browser-auth surface exposed only the staged `StartAsync`, `ActivateLoginEntryAsync`, and `ConfirmAndFillAsync` operations. The UI therefore waited for a separate confirmation at each email, password, and TOTP step. The browser session and profile lease were reusable, but there was no single-consent controller that advanced through supported states, enforced one-attempt scope, or prevented a second Auto Login for the same account.

The minimal fix reuses `BrowserAuthOrchestrator` and the existing Playwright session. It adds a bounded `AutoLoginAsync` workflow, in-memory scoped consent, preflight readiness, progress events, cancellation cleanup, and a concurrent-attempt gate. No new module or database migration was introduced.

## Implementation

### Contracts and consent

- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs:77-116` adds `AutoLoginAttemptConsent`, with attempt, account, provider, browser binding, issue/expiry, allowed actions, revocation, and consumption state.
- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs:124-135` adds `BrowserAuthPreflight`; `:136-139` adds `BrowserAuthProgress`.
- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs:218-230` adds `GetAutoLoginPreflightAsync` and `AutoLoginAsync` to `IBrowserAuthOrchestrator`.
- Consent is memory-only, expires with the attempt, is bound to the selected account/provider/profile binding, and is revoked on failure, cancellation, timeout, unsupported challenge, or scope mismatch.
- A per-account concurrent gate rejects a second Auto Login while the first attempt is active.

### Workflow

`BrowserAuthOrchestrator.AutoLoginAsync` keeps the same attempt, Playwright context, page, and profile lease while it advances through:

`OpeningBrowser -> CheckingExistingSession -> OpeningLogin -> FillingEmail -> FillingPassword -> FillingTotp -> CheckingAuthenticatedSession`.

- Existing authenticated UI is the fast path; no credential is read or filled.
- Guest landing activates the official login entry once.
- Email is read from the selected account metadata, then the scoped Continue action is activated.
- Password is read only through `BrowserAuthCredentialReader` after Vault readiness and account ownership checks.
- TOTP is generated locally from the selected account's Vault-bound seed using the existing RFC 6238 implementation.
- Each supported step verifies the active consent, account, profile binding, and trusted origin before filling.
- CAPTCHA, passkey, SSO, recovery code, SMS/email code, rate limiting, unknown states, invalid origin, browser closure, and unsupported transitions stop for human takeover or a categorized failure.
- Cancellation disposes the owned session, revokes consent, releases the profile lease, and preserves the persistent profile directory.

### Vault and secret handling

`src/Modules/Connections/Application/BrowserAuthCredentialReader.cs:62-112` performs non-secret readiness checks for preflight. Secret reads remain account-scoped and Vault-gated in the existing credential read path. Plaintext password, TOTP seed, generated code, cookies, local storage, tokens, and raw page HTML are not placed in attempt/result models or logs.

### UI

- `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor:27-112` now shows one consent screen with masked account, Vault state, credential-reference state, profile status, official destination, and the `Xác nhận và bắt đầu` action.
- `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor:847-898` loads preflight, subscribes to `BrowserAuthProgress`, starts one Auto Login operation, and renders stage messages.
- `src/Modules/Accounts/Features/AccountDetails/UI/AccountInformationTab.razor:78` exposes the account action as `Auto Login`.
- `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor.css` preserves isolated responsive styling for the consent and progress states.
- `tests/FlashAI.IntegrationTests/BrowserAuthPlaywrightE2ETests.cs` adds the headed Auto Login persistence case and uses a thread-safe progress-stage collector so asynchronous `Progress<T>` callbacks cannot race test assertions.

## Security and profile boundaries

- Integrated mode continues to start at `https://chatgpt.com/`; local mock login routes are used only by injected synthetic test composition.
- The production Playwright factory rejects non-official start destinations before ownership or browser launch.
- `ChromiumSandbox = true` remains enabled in the headed persistent-context launch.
- The same account-owned persistent profile is reused across the attempt and reopen check; Account A/B synthetic profiles remain isolated.
- No existing SQLite database, Vault, DPAPI/Data Protection key ring, account record, backup, or Chrome profile was selected, changed, migrated, or deleted.
- No real credential was read from chat, copied into source, command lines, fixtures, logs, screenshots, or this report.

## Test and build evidence

| Check | Result |
|---|---:|
| Debug architecture tests | PASS, 8/8 |
| Debug integration tests | PASS, 149/149, 0 skipped, 3m21s |
| Release architecture tests | PASS, 8/8 |
| Release integration tests | PASS, 149/149, 0 skipped, 3m36s |
| Focused Auto Login contract/integration coverage | PASS, 11/11 in the prior focused run; final `FullyQualifiedName~AutoLogin` rerun 7/7 |
| Headed synthetic Auto Login E2E | PASS, 1/1, approximately 14s |
| Debug solution build (final rerun) | PASS, 0 warnings, 0 errors |
| Release solution build (final rerun) | PASS, 0 warnings, 0 errors |

The headed persistence timing loop was repeated three times with the same synthetic local mock workflow. Actual observed durations were `13s`, `13s`, and `12s`. Stage-level timing instrumentation (`BrowserLaunchMs`, `EmailFillMs`, and similar fields) was not added, so no stage timings or invented median is reported.

The integration-test compilation still prints 7 pre-existing analyzer warnings in unrelated test code; the final solution builds themselves report 0 warnings and 0 errors. No new warning was introduced by the Auto Login implementation or the thread-safe progress collector.

## Runtime evidence

- Disposable direct host `http://127.0.0.1:5298` returned `200` for health, `/`, Blazor assets, and all 17 discovered CSS files, including module CSS.
- Visual Studio `http` launch profile served `http://localhost:5270`; health and root returned `200`.
- Visual Studio `https` launch profile served `https://localhost:7146` together with HTTP `5270`; health and root returned `200`.
- Headed synthetic Auto Login kept one browser session alive through guest landing, email, password, TOTP, authenticated UI observation, cancellation, and reopen persistence.
- Ports `5270`, `7146`, and `5298` had no listeners after cleanup, and disposable runtime roots were removed.

The public production route remains credential-free and official: the earlier headed probe reached `https://chatgpt.com/`, detected the site's login CTA, and reached the official email form without a password, TOTP, cookie, token, provider API call, or existing Chrome profile. This phase does not claim live account authentication.

## Final acceptance matrix

| Gate | Result |
|---|---|
| Auto Login button and one-click controller | GO |
| Single-consent workflow | GO |
| Account/profile/provider binding | GO, synthetic and DI/runtime verified |
| Existing-session fast path | GO, synthetic verified |
| Password and local TOTP autofill | GO, synthetic only |
| Human takeover boundary | GO |
| Chrome sandbox and ownership lifecycle | GO, headed synthetic verified |
| Persistent profile and account isolation | GO, headed synthetic verified |
| Architecture and integration regression | GO |
| ASP.NET Core runtime and static assets | GO, disposable host verified |
| Real ChatGPT credential acceptance | WAITING FOR USER / BLOCKED |
| Real authenticated-session persistence | WAITING FOR USER / BLOCKED |

## Required user action for live acceptance

Before any real attempt, independently rotate the exposed account password, reset/re-enroll its TOTP secret, invalidate the old credential, and save the replacement only in the local encrypted Vault. Then select exactly one account in FLASH ROUTER, review the masked preflight, and approve Auto Login in the application. Do not paste replacement secrets into Codex, terminals, reports, fixtures, or screenshots.

Until that in-app gate is completed, the implementation and synthetic certification are `GO`, while real credential acceptance and real session persistence remain `WAITING FOR USER / BLOCKED`.
