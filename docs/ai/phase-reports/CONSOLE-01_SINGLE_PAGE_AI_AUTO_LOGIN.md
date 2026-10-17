# CONSOLE-01 Single-Page AI Auto Login Console

Date: 2026-10-17  
Scope: single-page console UI, one-shot credential input, provider gating, ephemeral browser profiles, and sanitized runtime observability

## Executive verdict

- Single-page console implementation: **GO**
- Root route `/` and legacy account route separation: **GO**
- ChatGPT provider registry and unsupported-provider fail-closed behavior: **GO**
- One-shot credential parsing and memory-only lifetime: **GO**
- Temporary and keep-session profile modes: **GO offline**
- Sanitized bounded event console: **GO**
- Debug/Release build and regression suites: **GO**
- Real ChatGPT credential submission, authenticated identity, and session persistence: **NOT EXECUTED / WAITING FOR USER**

This phase does not import, inspect, or modify existing SQLite, Vault, DPAPI keys, account records, or Chrome profiles. The credential previously pasted into chat was not read, copied, entered, stored, or used as a fixture.

## Implementation

### Single-page navigation

- `FLASH ROUTER/Components/Pages/Home.razor:1-6` makes `/` render `AutoLoginConsole`.
- `FLASH ROUTER/Components/Pages/LegacyAccounts.razor:1` moves the prior account-manager wrapper to `/legacy/accounts` so the module-owned `/accounts` route remains unique.
- `FLASH ROUTER/Components/Console/AutoLoginConsole.razor:11-204` provides the complete responsive console: provider selection, masked one-line input, session mode, start/cancel actions, status card, severity filters, clear, auto-scroll, and sanitized log copy.

### Provider and workflow boundaries

- `src/Modules/Connections/Application/ConsoleProviderRegistry.cs:5-42` exposes ChatGPT as the only supported web-login provider. Claude, Gemini, and OpenRouter are visible as `PLANNED / UNSUPPORTED` and cannot start an attempt.
- ChatGPT starts through the existing orchestrator and official `BrowserOriginPolicy.ChatGptUri`; production Integrated mode does not select a local mock login URL.
- Local Mock E2E remains an injected test composition. It uses the existing browser-auth state machine and mock origin seam without changing production registration.

### Credential and profile lifetime

- `src/FlashAI.Foundation/Security/CredentialLineCodec.cs:9-149` parses one bounded line with strict `\\|` and `\\\\` escaping, rejects malformed delimiters and six-digit OTP values, and never echoes secret values in validation events.
- `src/Modules/Connections/Contracts/AutoLoginConsoleContracts.cs:9-34` stores the parsed credential in `EphemeralCredentialContext`; the UI clears the input immediately and clears the context in `finally`/`Dispose`.
- `src/Modules/Connections/Application/BrowserAuthOrchestrator.cs:232-399` runs the one-shot path without `AccountProfile`, SQLite, or Vault reads/writes. It reuses the existing origin, browser ownership, timing, human-takeover, TOTP, and cancellation boundaries.
- `src/Modules/Connections/Application/EphemeralBrowserProfileFactory.cs:19-70` creates a generated profile directory. `Temporary` profiles are bounded under the temp root and cleaned after the attempt; `KeepBrowserSession` profiles are kept under the application local session root for a later user-controlled open. Neither mode creates an account binding.

### Observability

- `src/Modules/Connections/Application/AutoLoginConsoleEventStream.cs:7-73` keeps a bounded event buffer, timestamps and sequences entries, redacts credential-like `key=value` fragments, filters by severity, and produces sanitized copy text.
- Cancellation waits for the orchestrator to release browser ownership. It does not kill Chrome, delete existing profiles, or broaden cleanup beyond a generated temporary directory.

## Verification evidence

| Check | Debug | Release |
|---|---:|---:|
| Solution build | PASS, 0 warnings / 0 errors | PASS, 23 analyzer warnings / 0 errors |
| Architecture tests | 8/8 | 8/8 |
| Full integration tests | 160/160 | 160/160 |
| Focused `Console01Tests` | 6/6 | 6/6 |
| Skipped tests | 0 | 0 |

The Release warnings are existing analyzer findings in application/test code; they do not represent compiler or test failures.

## Runtime smoke evidence

Earlier disposable-host checks exercised ports `5397` and `5398` with newly generated data roots. Readiness, `/`, the console marker, Blazor JavaScript, `FLASH_ROUTER.styles.css`, and `/legacy/accounts` returned HTTP 200; the console CSS marker was present. No user-owned host, SQLite/Vault root, DPAPI key ring, or Chrome process was terminated.

The configured Visual Studio launch profiles were already verified in the preceding runtime certification: `http://localhost:5270` and `https://localhost:7146` served the host and assets from disposable roots. A final listener check found no listener on `5397`, `5398`, `5270`, or `7146`.

## Acceptance matrix

| Gate | Result | Boundary |
|---|---|---|
| `/` renders the new console | GO | Root renders `AutoLoginConsole`. |
| Legacy account workflow remains reachable | GO | Available at `/legacy/accounts`; module `/accounts` is not duplicated. |
| Only ChatGPT can start | GO | Unsupported registry entries fail before orchestration. |
| Escaped credential line parsing | GO | Strict parser tests pass; six-digit OTP input is rejected. |
| No SQLite/Vault persistence for one-shot mode | GO | Memory-only context and ephemeral orchestrator path. |
| Temporary profile cleanup | GO offline | Generated disposable directory only. |
| Keep-session profile | GO offline | Generated application-owned session directory only. |
| Mock versus Integrated routing | GO offline | Mock origin is test-injected; production starts at official ChatGPT origin. |
| Real credential acceptance | NOT EXECUTED | Requires rotated secrets, local Vault update, selected account, and explicit in-app approval. |

## Follow-up boundary

This report certifies the console and offline wiring, not a real account login. A future live attempt must be one-account, user-initiated, and approved in FLASH ROUTER after independent password/TOTP rotation and local Vault update. CAPTCHA, SSO, passkey, MFA, recovery, email/SMS codes, and unknown challenges remain human actions. Do not paste replacement secrets into Codex, reports, terminals, logs, screenshots, fixtures, or source.

