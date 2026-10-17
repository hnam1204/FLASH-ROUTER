# Browser Profile Runbook

## Purpose

Open an official provider website in the Chrome profile owned by one FLASH ROUTER account without importing or inspecting browser session material.

## Ownership contract

Each managed profile has one in-process lease shared by the normal managed-Chrome launcher and the Playwright authentication session. The lifecycle is `Available -> OwnedByManagedChrome` or `OwnedByPlaywright -> Closed/Failed`; a second acquisition returns `PROFILE_IN_USE` and never starts another persistent context.

`SingletonLock`, `SingletonCookie`, and `SingletonSocket` are treated as lock metadata, not automatic proof of a live owner. The lock inspector distinguishes an active, non-shareable marker from stale metadata. Stale metadata is not deleted by FLASH ROUTER. A user-owned Chrome process is never killed and a lock file is never removed to force a launch.

## Safe flow

1. Select an existing account in the Accounts workspace.
2. Request the ChatGPT or official usage browser action.
3. Resolve the installed Chrome executable.
4. Load or create the account/provider/browser binding in SQLite.
5. Validate that the profile directory is inside the managed local data root.
6. Create the directory and launch Chrome with that directory and the official URL.
7. Record only launch state and timestamps.

## Credential-assisted account creation

When a user creates a ChatGPT web account with a manual credential line, the credential port writes the encrypted Vault reference and SQLite metadata first. It does not invoke the default browser or claim that authentication succeeded. If the user selected the browser option, the workspace then calls `BrowserProfileUiPort.OpenAsync` with the created `AccountId`, which opens `https://chatgpt.com/` in that account's managed profile.

The user completes email/password, MFA, CAPTCHA, and any other provider challenge themselves. A successful process launch means only that Chrome accepted the managed profile and official URL; it is not evidence of a live web session.

## AUTH-BROWSER-02 certification boundary

The certified headed Playwright fixture uses a local mock origin and an injected origin predicate only in the test composition. The production PlaywrightBrowserAuthenticationSessionFactory still accepts only the official HTTPS allowlist and remains the only factory registered by ConnectionsModuleRegistration.

The runtime certification covers profile persistence and account isolation through visible page-state observation after closing and reopening the managed profile. It does not inspect cookies, local storage, browser tokens, screenshots, or page HTML. Live ChatGPT login and provider verification remain outside the certified offline gate.

## Invariants

- A browser launch is not a login result.
- A browser launch is not usage verification.
- Never read, export, copy, or inspect cookies, local storage, profile tokens, or password-manager data.
- Never submit passwords or TOTP values automatically or in bulk.
- Reject account IDs that do not exist, unsupported providers, archived bindings, and path traversal.
- Keep profile directories separated by account and provider.
- On launch failure, persist a failure state and do not claim that the browser opened.
- Keep `ManualWebCredential`, `BrowserOpened`, `WebSessionUnknown`, and any later provider verification state separate.

## Recovery

- `CHROME_NOT_FOUND`: install or select the supported local Chrome installation, then retry.
- `BROWSER_PROFILE_PATH_INVALID`: do not edit the database manually; inspect the binding and repair through the application workflow.
- `PROFILE_IN_USE`: close the matching Chrome window or continue manually in that existing session; retry only after the owner is gone. Do not kill an unrelated process.
- `PROFILE_PERMISSION_DENIED`: verify access to the managed profile directory and the Chrome executable without changing the data root.
- `BROWSER_EXITED_EARLY` or `BROWSER_LAUNCH_TIMEOUT`: inspect the safe status message and retry once the local Chrome installation is healthy.
- `PLAYWRIGHT_DRIVER_MISSING`: restore the application dependency/runtime installation; do not substitute a private browser endpoint.
- `BROWSER_NAVIGATION_FAILED`: confirm the official HTTPS origin and local connectivity; never downgrade the origin policy.
- If website authentication is required, the user completes it in Chrome.

The application reports the category and a redacted recovery message. It does not expose command-line arguments, passwords, TOTP values, cookies, local storage, browser tokens, or raw Playwright dumps.

## Controlled assisted login

`Hỗ trợ đăng nhập` starts a separate Playwright headed context only when the user initiates it. The context owns the selected account's managed profile and navigates to `https://chatgpt.com/`; it does not attach to an existing Chrome Default profile or expose remote debugging.

The flow pauses at every sensitive step. The user must separately confirm email, password, and local TOTP filling. Password and TOTP are read only from the selected account's Vault reference after the Vault is unlocked. The TOTP code is generated locally with the existing RFC 6238 implementation and is not logged, stored in SQLite, or copied to the clipboard.

Before each sensitive fill, the current page must remain on an allowlisted official HTTPS origin. CAPTCHA, SSO, passkey, recovery code, SMS/email code, unknown challenges, invalid navigation, profile locks, and browser closure stop the flow and return control to the user. The orchestrator blocks repeated automatic fills and never submits the form or retries credentials.

`BrowserSessionObserved` records only a safe in-memory observation of visible official-provider UI. It is not `OAuthAuthorized`, `VerifiedIdentity`, subscription verification, usage verification, or API authorization. Automated coverage uses a synthetic browser session seam and disposable SQLite/Vault data; it does not use ChatGPT as a regression fixture.

## LIVE-ACCEPTANCE-02 operator gate

Before any real attempt, the operator must select one account, verify the masked account/provider/profile summary, confirm the official destination, and approve the specific guided action in the application. This prompt does not authorize unattended Vault unlock, credential submission, live ChatGPT access, or provider API traffic. If a previously shared secret may still be active, rotate it first.

The certified disposable-host UI evidence is limited to workspace render, Blazor circuit connection, and opening/closing the account dialog. It is not a real-account login result. A future live result must record `NOT EXECUTED`, `PARTIAL`, `BLOCKED`, or `VERIFIED` from actual visible browser state and must never infer identity, subscription, quota, or persistence from a launch alone.

## AUTH-BROWSER-11 runtime blocker recovery

The Auto Login dialog now distinguishes a stored credential reference from an unlocked/readable Vault payload. When the Vault is locked, use the dialog's existing Vault unlock action; do not enter the ChatGPT password in the Vault passphrase field. After unlock, the dialog refreshes preflight for the same `AccountId`. If an existing reference is unusable, use the explicit replacement flow for that account; it replaces the encrypted payload in place and preserves the connection/reference identity.

A persisted `LaunchFailed` status is historical evidence, not proof that the current retry will fail. The preflight reports it as retryable history. Start one user-initiated retry only after checking the safe profile path/ownership state. Do not delete profile locks, reset `BrowserProfileBindings`, kill `chrome.exe`, or replace an existing profile with a disposable directory.

Managed launches suppress Chrome first-run prompts with `--no-first-run`, `--no-default-browser-check`, and `--disable-sync`. These flags do not sign the user into Google Chrome and do not disable Chromium sandbox/security. If native Chrome onboarding still appears, the user must choose the appropriate onboarding action and then use the application retry/refresh control; FLASH ROUTER must never send ChatGPT credentials to browser onboarding.

AUTH-BROWSER-11 disposable runtime evidence used isolated roots under `.local/test-data/auth-browser-11-*`. It verified Host readiness, root/Connections routes, Blazor JS, all 17 discovered CSS assets, and both Visual Studio launch profiles. This evidence does not certify a real account, live provider credential, authenticated identity, or session reopen.

## LIVE-ACCEPTANCE-03 boundary

The current controlled acceptance is certified only with a disposable host, a local mock authentication origin, synthetic accounts, disposable Vault data, and headed Playwright. It covers the real production wiring and the user-facing state machine without opening a live ChatGPT account.

The live gate is deliberately one-account and in-app:

1. Select one account and review the masked provider, profile binding, and official destination.
2. Approve the exact guided action in FLASH ROUTER.
3. Complete CAPTCHA, SSO, passkey, MFA, recovery, email/SMS, or device approval manually when shown.
4. Observe only the visible provider state, close the owned profile, and reopen it if persistence is being accepted.

Do not treat the supplied credential, a Codex prompt, a browser launch, or `BrowserSessionObserved` as live consent. Real ChatGPT login and session persistence remain `NOT EXECUTED` in LIVE-ACCEPTANCE-03. If the credential previously pasted into chat is real, rotate its password and TOTP seed before any future user-controlled attempt.

The controlled evidence remains: headed E2E PASS, profile persistence/isolation PASS, official-origin production policy retained, and no cookie, local-storage, token, screenshot, or raw HTML inspection.

## AUTH-BROWSER-06 live acceptance gate

The current public routing acceptance is complete: Integrated User Mode starts at `https://chatgpt.com/`, uses the site's own login CTA, and reaches the official email form. It must never use the local mock `/login` route.

Authenticated acceptance requires a fresh password and TOTP rotation when a secret has been exposed, a local Vault update, one selected account with a masked profile summary, and explicit confirmation inside FLASH ROUTER for every sensitive action. A credential pasted into Codex is not consent and is not a test fixture. Password/TOTP submission, authenticated-session observation, and close/reopen persistence remain unexecuted until that gate is completed by the user.

## AUTH-BROWSER-07 certification boundary

The DI hotfix is certified through the complete host composition: the Integrated `BrowserProfileUiPort` registration uses an explicit factory for `IBrowserProfileManager` and `IChromeProcessLauncher`, while the legacy constructor remains available to direct callers. `builder.Build()` with `ValidateOnBuild` and `ValidateScopes` resolves the browser profile and browser-auth services successfully.

The runtime smoke pass used a disposable workspace root and both Visual Studio launch profiles. HTTP `5270` and HTTPS `7146` served readiness, the FLASH ROUTER root, Blazor assets, and the Connections module CSS bundle. The live browser boundary remains unchanged: Integrated User Mode starts at `https://chatgpt.com/`, discovers the site's own login CTA, and does not select a local mock `/login` URL.

The public route is certified only up to the official email form. Password, TOTP, authenticated-session observation, and close/reopen persistence are waiting for the user to rotate any exposed secret, update the local Vault, and approve each sensitive action in the application. Never use a credential pasted into Codex as live consent or as a test fixture.

## AUTH-BROWSER-08 one-click Auto Login

`Auto Login` is a single bounded attempt for one selected ChatGPT account. Before starting, FLASH ROUTER displays a masked account, Vault state, credential-reference readiness, managed profile status, official destination, and the actions covered by the one-time consent. The user approves once in the application.

The attempt then reuses one headed Playwright context and one account-owned persistent profile. It checks an existing authenticated UI first, then advances through the official login CTA, email, password from the unlocked Vault, and locally generated TOTP when those states are reliably detected. Each sensitive step rechecks consent scope, selected account, profile binding, and official origin.

Consent is memory-only and expires with the attempt. It is revoked on cancellation, timeout, failure, unsupported challenge, scope mismatch, or completion. A second Auto Login for the same account is rejected while the first is running. The flow never starts a background retry, bulk login, or a second browser context per step.

CAPTCHA, passkey, SSO, recovery code, SMS/email code, rate limiting, unknown state, browser closure, and invalid navigation stop for human takeover or a categorized failure. Chrome remains user-owned through the managed profile lease; cancellation releases the lease but preserves the profile directory.

The current certification is synthetic for credential autofill and persistence. Public production routing is verified only through the official ChatGPT landing page, login CTA, and email form. Real password/TOTP acceptance and real session persistence require rotated secrets in the local Vault and explicit in-app approval for one account. Do not use a credential pasted into Codex as consent or a fixture.

## AUTH-BROWSER-09 live-certification boundary

The AUTH-BROWSER-09 source audit found the AUTH-BROWSER-08 workflow intact: one managed Playwright context, one account/profile lease, official `https://chatgpt.com/` start, existing-session fast path, Vault-gated password, local TOTP, bounded human takeover, cancellation, and persistent-profile reopen behavior in synthetic E2E.

The configured Visual Studio `http` profile may be unavailable when another FLASH ROUTER process owns `5270`. Do not kill that process. Use a disposable alternate port only for isolated runtime smoke, record the collision, and leave the user-owned host untouched. A successful alternate-port smoke does not replace a live managed-account acceptance.

Public Chrome navigation to `https://chatgpt.com/` may observe an already authenticated browser profile. Treat that as public-origin and existing-profile evidence only. It does not prove the selected FLASH ROUTER account, managed profile binding, new credential acceptance, identity, subscription, or persistence.

Stage-level browser timings are not currently exposed. Report only measured endpoint or whole-workflow durations; do not invent per-stage medians. Real credential acceptance remains gated on password rotation, TOTP reset/re-enrollment, local Vault update, old-secret invalidation, and explicit one-time approval inside FLASH ROUTER.

## AUTH-BROWSER-10 timing and credential-rotation gate

The interactive Auto Login path now exposes nullable, redacted timing snapshots for the stages that actually ran: preflight, profile lease, Chrome launch, official navigation, existing-session check, login CTA, email, password, TOTP, authenticated observation, and total attempt. Synthetic sessions leave real-browser launch/navigation stages unset. Timing data contains elapsed milliseconds only; structured logs contain attempt/account/profile identifiers and timing values, never credentials or browser session material.

Before an Auto Login attempt can open Chrome, the user must attest in the FLASH ROUTER dialog that the password and TOTP were replaced after any prior exposure. The attestation is memory-only, carried by the one-attempt consent, and is required again for a later attempt. Missing attestation fails closed with `CREDENTIAL_ROTATION_ATTESTATION_REQUIRED` and does not open a browser or read Vault secrets.

The AUTH-BROWSER-10 evidence boundary is:

- Public official-origin navigation: verified at `https://chatgpt.com/` without credential submission.
- Disposable host: verified separately with health, root, Blazor, and module CSS checks.
- Synthetic Auto Login: verified with account-scoped Vault fixtures, local TOTP, bounded timing assertions, and attestation enforcement.
- Real credential submission, authenticated-session acceptance, and session reopen: `WAITING FOR USER` until the user independently rotates secrets, updates the local Vault, selects one account, and approves the in-app attempt.

## CONSOLE-01 one-shot console boundary

The root `/` console is an ephemeral workflow for one user-supplied credential line. It is separate from the account-owned workflow above:

- ChatGPT is the only enabled provider. Other visible provider choices are planning markers and must fail before a browser starts.
- The console parser accepts one bounded `account|password|TOTP seed` line with explicit escaping. It rejects malformed delimiters and a six-digit OTP value; it does not write the line to SQLite, AccountProfile, or Vault.
- The orchestrator receives an in-memory `EphemeralCredentialContext`, uses one headed browser context, and clears the context after completion or cancellation. Sensitive input is cleared from the UI immediately after validation.
- `Temporary` creates a generated profile under the disposable temp root and cleans that exact directory after the attempt. `Keep session` creates a generated application-owned session directory. Neither mode attaches to an existing Chrome profile or creates an account binding.
- The event console is bounded and sanitizes credential-like log fragments before display or clipboard copy. Do not add passwords, TOTP seeds, cookies, tokens, raw HTML, or screenshots to diagnostics.
- Production Integrated mode starts at `https://chatgpt.com/` through the official login CTA. Local mock login URLs are test-only seams and must never become a production destination through configuration.

The console is certified offline only. Real credential acceptance, authenticated identity, and session reopen remain user-controlled gates requiring rotated secrets, local Vault update, selected-account review, and explicit in-app approval.

## CONSOLE-02 managed Chromium runtime

The one-shot console resolves its browser engine through the solution's `Microsoft.Playwright 1.63.0` package and its built-in `Chromium.ExecutablePath`. The compatible Chromium revision is `1243` in `%LOCALAPPDATA%\\ms-playwright`. Do not replace this with installed system Chrome, a guessed `chromium-####` directory, or a profile owned by another application.

`Install / Repair Chromium` invokes the build-copied Playwright installer with `PLAYWRIGHT_SKIP_BROWSER_GC=1` scoped to that child process. It must not change the permanent environment or delete unrelated cache revisions. The headed session uses an app-owned generated profile and keeps Chromium sandboxing enabled.

The automation binary cache and app profile roots are separate. Existing `.local/data`, `.local/security`, `.local/browser-profiles`, and `.local/backups` remain compatibility roots and must not be migrated or deleted as part of runtime repair. If HuflitRegistrationTool coexistence has not been explicitly tested, record it as `NOT VERIFIED` rather than opening or modifying that tool's profiles.

