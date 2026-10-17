# AUTH-BROWSER-11 P0 Runtime Blocker Remediation

Date: 2026-10-17  
Scope: Tier 4 browser authentication readiness, Vault UX, managed Chrome launch recovery, DI/runtime verification, and synthetic headed acceptance

## Executive verdict

- Source remediation: **GO**
- DI composition and browser-auth service resolution: **GO**
- Credential readiness diagnostics: **GO for disposable/synthetic coverage**
- Embedded Vault unlock and credential replacement UI: **IMPLEMENTED; real-user transition not executed**
- Historical `LaunchFailed` recovery classification: **GO for regression coverage**
- Disposable Host runtime: **GO**
- Visual Studio `http` / `https` launch profiles: **GO**
- Headed synthetic browser acceptance: **GO**
- Real Vault unlock, real password/TOTP submission, authenticated identity, and session reopen: **WAITING FOR USER / NOT EXECUTED**
- Overall AUTH-BROWSER-11: **PARTIAL**

The phase fixes the runtime blockers in source and verifies a real ASP.NET Core Host with isolated data. It deliberately does not claim live authentication: the previously supplied credential is treated as exposed and was not read, copied, entered, logged, or placed in tests.

## Safety boundary

No real account, SQLite database, Vault ciphertext, DPAPI key ring, existing account record, existing browser profile, cookie, localStorage, token, raw page HTML, provider API, or real ChatGPT credential was accessed by this remediation. All Host runs used newly generated roots under `.local/test-data/auth-browser-11-*`. New processes were stopped with Ctrl+C after verification; no existing Visual Studio, Chrome, or Edge process was terminated.

## Root causes

### 1. Credential readiness was too coarse and ordered incorrectly

`BrowserAuthCredentialReader.CheckReadinessAsync` resolved missing/provider-mismatched references before checking the Vault gate. That caused an account with no reference and a locked Vault to report `CREDENTIAL_NOT_FOUND`, while the user-facing blocker was correctly “Vault đang khóa”. The reader now checks the selected account, resolves any matching reference for safe identity continuity, then returns `VAULT_LOCKED` before exposing reference-missing/provider-mismatch details. Once unlocked, it distinguishes missing reference, payload unavailable, ownership mismatch, provider mismatch, read failure, and ready states without exposing secret material.

### 2. Credential replacement used a SQLite-incompatible ordering expression

`SqliteAccountCredentialUiPort.ReplaceAsync` ordered `DateTimeOffset` directly in an EF Core SQLite query. SQLite rejected that translation at runtime. The query now materializes the narrow candidate set and orders it in memory, preserving the newest existing manual-web connection while atomically replacing the same Vault entry. `ConnectionId` and credential-reference identity remain unchanged.

### 3. Historical profile failure was presented as a current blocker

`BrowserAuthOrchestrator.GetAutoLoginPreflightAsync` now labels a persisted `LaunchFailed` as historical and exposes `BrowserProfileRetryAvailable`. A current attempt still records a fresh failure through the existing profile manager and ownership boundary. No stale lock file, profile, database row, or Chrome process is deleted or reset.

### 4. The dialog lacked the recovery controls and had a progress binding defect

`BrowserAuthDialog` now offers the existing application Vault unlock path when locked and a narrow encrypted credential replacement path when a reference is present but unusable. Passphrase/password/TOTP input fields are cleared after use. `MainWorkspace` refreshes preflight after successful Vault unlock or replacement while retaining the selected `AccountId`. The dialog receives `ProgressMessage` as an explicit Razor expression, so `_browserAuthProgressMessage` is not rendered as literal text.

## Source changes

- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs:22` adds typed readiness categories and carries credential, connection, account, provider, and profile identity through the in-memory attempt/preflight contracts.
- `src/Modules/Connections/Application/BrowserAuthCredentialReader.cs:77` performs the Vault-first readiness gate and safe payload validation. It zeroes temporary payload buffers and never returns password/TOTP material in readiness DTOs.
- `src/Modules/Connections/Application/SqliteAccountCredentialUiPort.cs:136` adds encrypted in-place replacement. `:167-185` avoids SQLite `DateTimeOffset` ordering translation while preserving the newest existing connection/reference.
- `src/Modules/Connections/Application/BrowserAuthOrchestrator.cs:45` reports precise preflight state, historical profile retry availability, and structured safe BrowserAuth events. Attempt state remains bound to immutable `AttemptId`, `AccountId`, provider, and profile binding.
- `src/Modules/Connections/Application/ChromeProcessLauncher.cs:76` adds first-run/sync suppression flags for managed disposable/account-owned Chrome launches (`--no-first-run`, `--no-default-browser-check`, `--disable-sync`). Sandbox/security flags are not disabled.
- `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor:46` adds the existing Vault unlock control; `:69` adds replacement controls; `:197-216` clears sensitive UI values after use; `:225` maps typed readiness to truthful labels.
- `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor:197-200` binds the progress value explicitly; `:880-952` reuses `IAuthenticatorUiPort`, refreshes preflight, and preserves the selected account after local recovery.
- `tests/FlashAI.IntegrationTests/AuthBrowser11Tests.cs:43` adds readiness, identity-preserving replacement, and historical-failure retry regressions.
- `tests/FlashAI.IntegrationTests/FlashRouterHostCompositionTests.cs:24` builds the complete Integrated service graph with `ValidateOnBuild=true` and `ValidateScopes=true`, then resolves the browser services.

## Runtime evidence

### Disposable Integrated Host

Release Host started at `http://127.0.0.1:5292` with a newly generated workspace root and logged `Application started`. Fresh probes returned:

| Endpoint/check | Result |
|---|---:|
| `/health/ready` | HTTP 200 |
| `/` | HTTP 200 |
| `/connections` | HTTP 200 |
| `/_framework/blazor.web.js` | HTTP 200 |
| Discovered global and module CSS assets | 17/17 HTTP 200 |

The in-app browser rendered `FLASH ROUTER | Account Manager` at the disposable root with the actual empty-account workspace UI. The `/connections` route rendered the Connections page and module styling. No mock login route was used as runtime evidence.

### Launch profiles

- `http`: `http://localhost:5270` started with `ASPNETCORE_ENVIRONMENT=Development`; health, root, and Blazor runtime returned HTTP 200.
- `https`: `https://localhost:7146` and `http://localhost:5270` started together; HTTPS health, root, and Blazor runtime returned HTTP 200 with the local development certificate accepted only by the local probe.

Both launch-profile runs used disposable roots and were stopped gracefully. No real host data was selected.

### Browser runtime

The headed Playwright suite uses real browser interaction against synthetic local fixtures and disposable profiles. It verifies profile/session lifecycle, guest landing, login-entry detection, bounded state transitions, human-takeover states, browser closure, and account-owned persistence. It does not use a real ChatGPT account or submit any provider credential.

Native Chrome first-run onboarding was not reproduced against an existing user profile. The launcher hardening is limited to the managed launch flags above; no Google Chrome sync/sign-in was automated and no enterprise/security barrier was bypassed.

## Test evidence

All counts below are fresh in this phase:

| Check | Debug | Release |
|---|---:|---:|
| Solution build | PASS, 0 warnings / 0 errors | PASS, 38 analyzer warnings / 0 errors |
| Architecture tests | 8/8 | 8/8 |
| Full Integration tests | 154/154 | 154/154 |
| Headed `BrowserAuthPlaywrightE2ETests` | 13/13 | 13/13 |
| Focused AUTH-BROWSER-01 + AUTH-BROWSER-11 | 16/16 | covered by the same source; focused run was Debug |
| Full-host DI validation | PASS | covered by full suite |

The Release warnings are analyzer warnings in existing/source areas, not build errors. No test was skipped. The 13-test headed filter includes the 60-second guest-session lifecycle test and completed in approximately 2m56s Debug and 3m03s Release.

## Acceptance matrix

| Gate | Result | Evidence / remaining boundary |
|---|---|---|
| 11A Vault unlock in dialog | PARTIAL | UI and existing Vault port are implemented; no real/disposable passphrase was entered in this run. |
| 11B Credential readiness | GO offline | Typed readiness, Vault gate, payload/ownership checks, and replacement identity regression pass. |
| 11C LaunchFailed forensics | PARTIAL | Historical failure is retryable and regression-covered; the real user binding was not inspected or modified. |
| 11D Chrome first-run | PARTIAL | Managed flags and headed synthetic lifecycle pass; native first-run was not automated or reproduced on an existing profile. |
| 11E Account/profile binding | GO offline | Attempt identity and host DI tests preserve account/profile scoping; no NumericId inference is used. |
| 11F Razor progress | GO source/runtime smoke | Explicit expression binding is compiled and disposable UI renders; no literal `_browserAuthProgressMessage` was observed. |
| 11G Recoverable Auto Login | PARTIAL | Synthetic workflow passes; real Vault/credential boundary remains user-controlled. |
| 11H Structured diagnostics | GO source coverage | Safe BrowserAuth event logging includes attempt/account/profile/state/error category and excludes secrets. |
| 11I Real Host runtime | GO disposable / PARTIAL real-user | Host, routes, Blazor assets, CSS, and both launch profiles pass on isolated roots; real account flow not executed. |
| 11J Regression | GO | Builds, architecture, integration, focused, and headed suites pass as listed above. |
| Data preservation | GO | Only disposable roots were created; no user data/profile/Vault/DPAPI mutation occurred. |

## Live boundary and next action

Official ChatGPT password/TOTP submission, authenticated identity observation, and session reopen are **NOT EXECUTED** in this phase. The previously supplied values are not valid test fixtures or consent. Before any user-controlled live attempt, independently rotate the exposed provider password, reset/re-enroll MFA, invalidate the old secret, update only the replacement values through FLASH ROUTER's encrypted Vault UI, select exactly one account, unlock the Vault in the app, and approve one bounded Auto Login attempt. CAPTCHA, passkey, SSO, recovery, SMS/email code, and device approval remain human actions.

## Final verdict

**AUTH-BROWSER-11 source/runtime blocker remediation: PARTIAL.** The reported readiness, replacement, historical-launch, DI, progress-rendering, and Host-startup blockers are repaired and regression-tested. A full live-authentication GO is intentionally withheld because no real rotated credential, Vault unlock, selected-account identity acceptance, or session reopen was authorized or executed.
