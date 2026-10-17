# AUTH-BROWSER-01 — Controlled ChatGPT Browser Login

Date: 2026-10-16  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`  
Verdict: `PARTIAL`

## 1. Actual source baseline

Before this phase, FLASH ROUTER already had SQLite account persistence, DPAPI/Data Protection Vault storage, local RFC 6238 TOTP generation, account-scoped Chrome profile bindings, safe Chrome launch, official OAuth security contracts, and provider capability/provenance models. The existing browser path opened `https://chatgpt.com/` but did not automate or observe login steps.

The workspace directory is not currently recognized as a Git checkout. No repository was initialized and no commit, push, deployment, database reset, Vault reset, DPAPI reset, or Chrome-profile deletion was performed.

## 2. Files changed

- `src/FlashAI.Foundation/Security/ILocalTotpCodeGenerator.cs` and `Base32SecretCodec.cs` expose the architecture-safe local TOTP seam.
- `src/Modules/Authenticator/Application/Rfc6238TotpGenerator.cs` and `AuthenticatorModuleRegistration.cs` reuse the BE-02 generator through that seam.
- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs` adds typed browser-auth states, results, session, page-detector, and orchestrator contracts.
- `src/Modules/Connections/Application/BrowserProfileManager.cs` centralizes profile preparation and launch-state recording; `BrowserProfileUiPort.cs` delegates to it.
- `src/Modules/Connections/Application/BrowserAuthCredentialReader.cs`, `BrowserAuthPageStateDetector.cs`, `PlaywrightBrowserAuthenticationSession.cs`, and `BrowserAuthOrchestrator.cs` implement the controlled flow.
- `src/Modules/Connections/FlashAI.Modules.Connections.csproj` adds `Microsoft.Playwright` 1.63.0.
- `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor` and `.razor.css`, `MainWorkspace.razor`, and `AccountInformationTab.razor` add the one-page user-confirmation UI.
- `tests/FlashAI.IntegrationTests/AuthBrowser01Tests.cs` adds synthetic Vault/SQLite/browser-session coverage.
- Updated `docs/handover/CODEX_HANDOFF.md`, `docs/runbooks/browser-profile.md`, `docs/security/credentials-handling.md`, and `docs/architecture/provider-capability-matrix.md`.

## 3. GitHub references studied

- `Lampese/codex-switcher`: reviewed account-switching and attempt-lifecycle concepts only; no source, client ID, token, or auth file was copied.
- `jlcodes99/cockpit-tools`: reviewed provider isolation and quota/provenance concepts only; no source or account-rotation logic was copied.
- `microsoft/playwright-dotnet`: used the official headed Chromium/Chrome persistent-context and locator API model. The implementation uses a system Chrome executable and does not expose remote debugging.
- `kspearrin/Otp.NET`: reviewed RFC 6238, Base32, time period, and remaining-window semantics. No package was added because BE-02 already provides a local RFC 6238 implementation.
- License metadata for the two account-manager references was not available through the GitHub API during the audit, so both were treated as research-only references and no code was copied.

## 4. Browser automation architecture

`IBrowserAuthOrchestrator` coordinates a short-lived in-memory attempt. `IBrowserProfileManager` prepares the account-owned profile. `IBrowserAuthenticationSessionFactory` owns a headed Playwright persistent context. `IBrowserPageStateDetector` maps a safe page snapshot to a supported state. `BrowserAuthCredentialReader` is the only browser-auth path that can resolve the selected account's Vault reference.

The state machine distinguishes `BrowserStarting`, `Navigating`, `EmailRequired`, `PasswordRequired`, `MfaRequired`, `ExternalProviderRequired`, `HumanActionRequired`, `BrowserSessionObserved`, `ProfileInUse`, `Failed`, `Cancelled`, `TimedOut`, and `Unknown`.

## 5. Chrome profile handling

The existing per-account SQLite binding remains the source of profile ownership. The new manager shares its preparation logic with the existing manual `Mở Chrome` action. Playwright receives only the managed profile directory and resolved Chrome executable. `SingletonLock`, `SingletonCookie`, and `SingletonSocket` cause `ProfileInUse`; the implementation does not kill Chrome, copy a live profile, attach to Default, or delete lock material.

## 6. Email step

Email detection uses stable attributes such as `id`, `name`, `autocomplete`, `aria-label`, `placeholder`, and `type`; it does not use generated CSS classes or coordinates. The email comes from the selected `AccountProfile`. A separate UI action is required before filling.

## 7. Password step

Password fill requires the selected account, the official origin, a recognized password field, an unlocked Vault, an existing manual-web credential reference, and an explicit confirmation. The reader verifies that the Vault credential email matches the selected account email. Passwords are not put into result models or logs, and repeated automatic fills are blocked.

## 8. MFA step

Authenticator fields use stable one-time-code, numeric-input, aria-label, and name attributes. The flow requires an unlocked Vault and a TOTP seed belonging to the selected account. The user confirms the action before a single code is generated and filled.

## 9. Local TOTP implementation

`Rfc6238TotpGenerator` from BE-02 remains the generator. `Base32SecretCodec` is placed in Foundation because module boundaries prohibit a Connections-to-Authenticator reference. Generated codes are transient, are not persisted, logged, or copied to the clipboard, and the decoded secret buffer is zeroed after generation.

## 10. Manual takeover

CAPTCHA, SSO, passkey, recovery code, SMS/email code, browser closure, unknown state, and untrusted navigation return a safe stop state. The UI tells the user to complete the challenge in Chrome and provides an explicit refresh/observe action. The flow never bypasses or retries these challenges.

## 11. Session observation

When the visible page contains a supported authenticated marker on an allowed origin, the result is `BrowserSessionObserved`. The observation includes only account/profile/provider IDs, UTC observation time, method, and safe navigation status. It does not read cookies, local storage, session keys, tokens, HTML, or screenshots.

## 12. Account data provenance

Manual email remains user-provided/local metadata. Stored credentials remain `CredentialStored`. A visible post-login UI is `BrowserSessionObserved`; it is not `OAuthAuthorized`, `VerifiedIdentity`, `Subscription`, `Usage`, `Quota`, `Credits`, or API authorization.

## 13. Provider capability limitations

No ChatGPT Web subscription, usage, quota, credits, reset time, or verified identity is inferred from browser state. Existing API adapters remain separate from ChatGPT Web. Official OAuth remains governed by its own eligibility and callback checks.

## 14. SQLite/Vault safety

No migration was required. Browser-auth attempts stay in memory. SQLite retains only the existing account, connection, credential-reference, and browser-profile metadata. Vault payloads are read only after access-state approval and are never copied into SQLite or browser-auth result records.

## 15. Build results

`dotnet restore "FLASH ROUTER.sln"` reported all projects up to date. `dotnet build "FLASH ROUTER.sln" -p:UseAppHost=false --nologo` completed successfully with `0` errors. No apphost copy was attempted, avoiding the prior locked `FLASH_ROUTER.exe` failure.

## 16. Test results

Focused AUTH-BROWSER-01 integration tests pass `5/5`. Full solution regression passes architecture `8/8` and integration `114/114`.

## 17. Browser E2E

The synthetic controlled-session seam covers account creation, encrypted credential save, explicit email/password/TOTP confirmation, Vault lock rejection, account ownership, local TOTP, authenticated UI observation, untrusted origin, CAPTCHA takeover, profile lock, and cancellation. This is `GO` for the deterministic orchestration seam. A headed Playwright run against a disposable local mock authentication site was not executed in this phase, so the full offline browser E2E verdict remains `PARTIAL`.

## 18. Responsive results

The existing one-page responsive matrix remains certified at `375x812`, `768x1024`, `1024x768`, `1280x800`, and `1440x900` from prior phases. The new dialog includes a narrow viewport layout, but a fresh visual capture matrix for this phase was not executed.

## 19. Live acceptance status

Real ChatGPT login: `NOT EXECUTED`. The supplied credential was not used, stored, tested, or repeated. If it is genuine, rotate the password and TOTP secret. Any future live acceptance must be user-assisted, one selected account at a time, with the user completing every unsupported challenge.

## 20. Known blockers

- A real Chrome/ChatGPT flow has not been accepted or verified.
- The current local environment was not used to start a live app against real data.
- Full headed Playwright and local mock-site E2E require a disposable browser runtime and were not run here.
- Official OAuth, subscription, usage, quota, and credits remain independent and unverified.

## 21. Final verdicts

- `AUTH-BROWSER-01A ARCHITECTURE — GO` (typed state machine and security boundaries implemented).
- `AUTH-BROWSER-01B PROFILE MANAGEMENT — GO OFFLINE` (shared managed profile preparation and lock handling; live headed launch not run).
- `AUTH-BROWSER-01C EMAIL — GO OFFLINE` (synthetic confirmation and stable-field coverage).
- `AUTH-BROWSER-01D PASSWORD — GO OFFLINE` (Vault gate, ownership check, explicit confirmation, no retry).
- `AUTH-BROWSER-01E LOCAL TOTP — GO OFFLINE` (existing RFC 6238 generator, synthetic flow).
- `AUTH-BROWSER-01F POST-LOGIN — GO OFFLINE` (visible UI observation only).
- `AUTH-BROWSER-01G ACCOUNT DATA — PARTIAL` (provenance boundaries implemented; no live provider data).
- `AUTH-BROWSER-01H UX — PARTIAL` (dialog implemented; fresh responsive capture not rerun).
- `AUTH-BROWSER-01I RUNTIME — PARTIAL` (build passes; live runtime/real-data acceptance not executed).
- `OFFLINE BROWSER E2E — PARTIAL` (synthetic session seam `5/5`; local mock headed run pending).
- `REAL CHATGPT LOGIN — NOT EXECUTED`.
- `OFFICIAL OAUTH — NOT EXECUTED`.
- `REAL CHATGPT QUOTA — UNSUPPORTED / NOT EXECUTED`.
- `WINDOWS CLI COMPATIBILITY — GO BUILD / LIVE ACCEPTANCE PENDING`.
- `OVERALL AUTH-BROWSER-01 — PARTIAL`.
