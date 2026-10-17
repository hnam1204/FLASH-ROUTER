# CONSOLE-02 - Automation-First Playwright-Managed Chromium

Date: 2026-10-17  
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`  
Status: **PARTIAL / OFFLINE GO**

## Scope and safety boundary

This report implements the attached CONSOLE-02 specification and records only offline, disposable-runtime, and credential-free public-site evidence. The previously pasted account values were not read back, logged, used as fixtures, submitted to a provider, or stored. No Vault, SQLite database, DPAPI key ring, existing Chrome profile, provider API, HuflitRegistrationTool, Tier 5 usage work, Tier 6 packaging, GitHub, installer, PATH, commit, push, or deployment work was performed.

The source requirement is preserved at `docs/ai/requirements/FLASH_ROUTER_AUTOMATION_FIRST_IMPLEMENTATION_MASTER.md`.

## Source map and implementation

The existing automation path remains feature-first and is now runtime-backed by the Playwright package shipped with this solution:

| Area | Current implementation | Evidence |
| --- | --- | --- |
| One-page console | `/` renders `AutoLoginConsole`; legacy account management remains at `/legacy/accounts`. | `FLASH ROUTER/Components/Console/AutoLoginConsole.razor` |
| Ephemeral input | Strict escaped `EMAIL|PASSWORD|2FA` parser and in-memory attempt context. | `src/FlashAI.Foundation/Security/CredentialLineCodec.cs`, `src/Modules/Connections/Contracts/AutoLoginConsoleContracts.cs` |
| Browser runtime | `PlaywrightBrowserRuntime` calls `Playwright.CreateAsync()` and reads `Chromium.ExecutablePath`; it never selects system Chrome. | `src/Modules/Connections/Application/PlaywrightBrowserRuntime.cs:13` |
| Browser launch | Headed `Chromium.LaunchPersistentContextAsync` with `ChromiumSandbox = true` and no `ExecutablePath`; profile directories are app-owned. | `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:72` |
| Runtime contract | Readiness, package version, cache path, expected revision, resolved revision, and install/repair status are typed. | `src/Modules/Connections/Contracts/BrowserProfileContracts.cs:110` |
| Temporary/kept profiles | `%LOCALAPPDATA%\\FlashRouter\\BrowserProfiles`, with `.temp` for one-shot sessions and generated directories for explicit keep-session mode. | `src/Modules/Connections/Application/EphemeralBrowserProfileFactory.cs:6` |
| Legacy data compatibility | `LocalDataPaths` keeps `.local/data`, `.local/security`, `.local/browser-profiles`, and `.local/backups` unchanged; automation roots are separately overridable. | `src/FlashAI.Foundation/Persistence/LocalDataPaths.cs:3` |
| DI | `BrowserProfileUiPort` keeps both compatibility constructors, while registration uses an explicit factory for the canonical `IBrowserProfileManager` + `IChromeProcessLauncher` constructor. | `src/Modules/Connections/ConnectionsModuleRegistration.cs:86` |
| Host validation | Complete Integrated composition uses `ValidateOnBuild = true` and `ValidateScopes = true`, then resolves browser/auth services. | `tests/FlashAI.IntegrationTests/FlashRouterHostCompositionTests.cs:14` |

The browser-auth flow is `BrowserAuthDialog`/console -> `BrowserAuthOrchestrator` -> `PlaywrightBrowserAuthenticationSessionFactory` -> Playwright-managed Chromium. Integrated mode accepts only the official ChatGPT landing origin. Local mock E2E supplies a test-only session factory and remains isolated from the integrated destination.

## Playwright runtime evidence

- Package: `Microsoft.Playwright 1.63.0` (assembly reports `1.63.0.0`).
- Package metadata selects Chromium revision `1243` (`Chrome for Testing` browser version `153.0.8010.12`).
- The compatible managed cache is `%LOCALAPPDATA%\\ms-playwright`; revision `chromium-1243` and `chromium_headless_shell-1243` are installed.
- Existing cache revisions `1161`, `1228`, and `1248` were not deleted.
- The runtime status observed by the rendered console was `READY`, package `1.63.0.0`, revision `1243`, cache `ms-playwright`.
- A regression test verifies that the resolved executable path contains `ms-playwright`, matches the package expected revision, and does not contain `Google\\Chrome`.
- Install/Repair runs the build-copied `playwright.ps1` with `PLAYWRIGHT_SKIP_BROWSER_GC=1` scoped to the child process. It does not modify permanent environment settings.
- Headed launch settings keep sandbox enabled and do not use a `--no-sandbox` fallback.

The first full-suite attempt exposed a stale test output problem: the output directory lacked `.playwright/package/cli.js`, causing one dynamic skip/failure. Rebuilding the test project copied the package runtime files; the focused retry passed, and the final Debug/Release full suites passed with no skips.

## Data and process isolation

The active disposable host used a generated root under `.local/test-data/console-02-runtime-*` and an automation override beneath that root. Its `/local` roots were not the user's existing data roots. Read-only inspection confirmed the existing `.local/data`, `.local/security`, `.local/browser-profiles`, and `.local/backups` directories remain present. No migration, merge, reset, or deletion was attempted.

The disposable runtime directories remain because the environment rejected the cleanup command's recursive-delete policy. They contain only disposable host migration/runtime artifacts; they are not existing user data roots. No listener remains on disposable port `5297` or HTTPS port `7146`. At final inspection, HTTP port `5270` was occupied by a Visual Studio-owned `FLASH_ROUTER.exe` process (PID `32168` via `VsDebugConsole`); it was not terminated or modified.

HuflitRegistrationTool concurrency was **NOT VERIFIED** because the tool was not inspected, started, modified, or disrupted, as required by scope.

## Host and UI runtime evidence

A real Integrated ASP.NET Core host was started with a disposable data root at `http://127.0.0.1:5297`. The host applied migrations to the disposable SQLite database and served:

- `/health/ready`: HTTP `200`
- `/`: HTTP `200`
- `/connections`: HTTP `200`
- `/_framework/blazor.web.js`: HTTP `200`
- 17 discovered CSS assets (foundation, host, and module bundles): `17/17` HTTP `200`

The rendered page was inspected in a browser and showed `FLASH ROUTER | AI Auto Login Console`, the ChatGPT `VERIFIED WEB` selector, Playwright package `1.63.0.0`, revision `1243`, `READY`, masked credential input, Temporary/Keep session controls, Start/Cancel/Retry controls, and the live console. The page remained at the local host; no credentials were entered.

The Visual Studio launch profiles remain local-only in `FLASH ROUTER/Properties/launchSettings.json`. The `http` profile returned `200` for `/health/ready` and `/` on `http://localhost:5270`. The `https` profile returned `200` for readiness and `/` on `https://localhost:7146`, and readiness also returned `200` on its HTTP companion `5270`. This is host startup evidence only; no claim is made that a browser window proves authenticated provider access.

## Tests and builds

Final results after the runtime regression test was added:

| Gate | Debug | Release |
| --- | ---: | ---: |
| Solution rebuild | 0 errors, 75 warnings | 0 errors, 75 warnings |
| Architecture tests | 8/8, 0 skipped | 8/8, 0 skipped |
| Full integration tests | 161/161, 0 skipped | 161/161, 0 skipped |
| CONSOLE-01 focused tests | 7/7 | included in full suite |
| Managed-Chromium runtime regression | pass | pass |

Release warnings are analyzer warnings in existing code and tests (for example CA1822, CA1848, CA1861, CA1707, CA1859, CA1305, and CA1805); there were no compile errors and no new runtime failure. The full headed Playwright E2E set is included in the 161 integration tests and passed in both configurations.

## Acceptance matrix

| Requirement | Result | Notes |
| --- | --- | --- |
| One-page console | PASS | Real browser render observed. |
| `EMAIL|PASS|2FA` quick input | PASS | Escaping, optional field, redaction, bounded events tested. |
| No mandatory Vault/SQLite for one-shot | PASS | Ephemeral path uses disposable in-memory credential context. |
| Playwright-managed Chromium | PASS | Revision 1243 resolved by Playwright; no system Chrome fallback. |
| Chromium install/repair | PARTIAL | Installer path and child-process safety implemented; missing-runtime repair was not destructive-tested against the shared cache. |
| Shared binary cache preserved | PASS | Existing revisions were retained. |
| Profile/process isolation | PASS | App-owned generated profiles; ownership and headed E2E coverage pass. |
| Huflit tool unchanged | NOT VERIFIED | Explicitly out of scope for inspection/concurrency run. |
| ChatGPT web adapter | PASS (offline/public entry) | Official landing/login/email states covered; no credential submission. |
| Live console events | PASS | Bounded typed/redacted events and browser-rendered console verified. |
| Start/Stop/Cancel | PASS (offline) | Synthetic headed cancellation and terminal-state coverage pass. |
| Automation security | PASS (offline) | Origin checks, consent, Vault gate, redaction, and human-takeover boundaries pass. |
| Synthetic headed E2E | PASS | Included in 161/161 in Debug and Release. |
| Official public navigation | VERIFIED | Prior/current credential-free managed-browser coverage reaches official ChatGPT entry; no token query recorded. |
| Real authenticated session | NOT EXECUTED / WAITING FOR USER | Requires rotated credentials and explicit in-app approval. |
| Real session persistence | NOT EXECUTED / WAITING FOR USER | Not certified without the preceding live authenticated session. |
| Existing user data | PRESERVED | Existing legacy roots remain present; no migration or deletion. |
| Overall offline | PASS | Build, DI, tests, runtime host, and UI evidence pass. |
| Overall live | BLOCKED / NOT EXECUTED | Deliberately stopped before real secret submission. |

## Next action

Before any live account test, the user must rotate the previously exposed password, reset/re-enroll MFA, update the local Vault through the application, and explicitly approve one one-shot attempt. This phase does not authorize or perform that action.
