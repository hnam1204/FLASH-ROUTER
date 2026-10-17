# LIVE-AUTH-01 Chrome Account Acceptance

Date: 2026-10-16  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`

## Verdict

`PARTIAL`.

The offline ownership, launch-diagnostics, headed Playwright, persistence, and host-startup gates are GO. Real ChatGPT account login and real session-retention acceptance are `NOT EXECUTED`. No supplied credential, real account, live ChatGPT login, cookie, token, or existing Chrome profile was used.

## Root cause and reproduction boundary

The exact historical failure against a live user profile could not be reproduced safely. Read-only inspection found two managed profile directories, no `SingletonLock`, `SingletonCookie`, or `SingletonSocket`, and no Chrome process command line referencing those directories. No lock file was removed and no Chrome process was terminated.

The confirmed deterministic defect was the ownership and diagnostics boundary:

1. `Mở Chrome` and Playwright did not share a per-profile ownership lease, so both paths could try to use one persistent Chrome user-data directory.
2. Any lock marker could be treated as active even when it was stale metadata.
3. Broad process and Playwright failures were collapsed to a generic launch error, preventing safe recovery.

The separate P0 startup defect was also confirmed: implicit DI construction saw both public `BrowserProfileUiPort` constructors as resolvable and failed with constructor ambiguity during `builder.Build()`.

## Minimal implementation

- `src/Modules/Connections/ConnectionsModuleRegistration.cs:78-83` uses an explicit factory and resolves the canonical `IBrowserProfileManager` plus `IChromeProcessLauncher` constructor.
- `src/Modules/Connections/Application/BrowserProfileUiPort.cs:14-38` preserves the legacy constructor for existing callers while retaining the canonical DI constructor.
- `src/Modules/Connections/Application/BrowserProfileOwnership.cs:6-145` distinguishes no lock, stale metadata, active lock, and an in-process lease; canonical paths are synchronized per profile.
- `src/Modules/Connections/Application/ChromeProcessLauncher.cs:61-148` returns categorized failures, acquires a managed-Chrome lease, and releases it when the process exits or launch fails.
- `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs:20-166` acquires the same ownership coordinator, stops on profile contention, and releases the lease on cancellation, failure, or session disposal.
- `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor:71-75` exposes a truthful profile-in-use recovery message instead of claiming a successful login.

Failure categories include `CHROME_NOT_FOUND`, `PROFILE_IN_USE`, `PROFILE_PERMISSION_DENIED`, `INVALID_PROFILE_PATH`, `BROWSER_EXITED_EARLY`, `BROWSER_LAUNCH_TIMEOUT`, `BROWSER_NAVIGATION_FAILED`, and cancellation/unknown failures. Raw command lines, credentials, browser storage, and provider responses are not exposed.

## Offline evidence

| Check | Debug | Release |
| --- | ---: | ---: |
| Architecture tests | 8/8 | 8/8 |
| Full integration tests | 127/127 | 127/127 |
| Solution build | 0 warnings, 0 errors | 0 warnings, 0 errors |

Focused ownership/diagnostics coverage includes available profiles, managed ownership, active external locks, stale lock metadata, concurrent acquisition, Unicode paths, early browser exit, cancellation, and Playwright refusal before launch. The host composition test builds with `ValidateOnBuild=true` and `ValidateScopes=true`, then resolves browser-profile and browser-auth services.

The headed local-mock Playwright suite passed `4/4` in the preceding AUTH-BROWSER-02 certification. It covered profile persistence, account isolation, human takeover states, invalid password/TOTP transitions, and browser closure. It did not use ChatGPT or a real account.

## Runtime evidence

The HTTP launch profile started the real host at `http://localhost:5270`. `/health/ready`, `/`, Blazor JavaScript, host CSS, and all 11 module CSS bundles returned HTTP 200. The HTTPS launch profile started at `https://localhost:7146`; `/health/ready`, `/`, Blazor JavaScript, and host CSS returned HTTP 200. These checks used disposable runtime data roots and the Visual Studio launch profiles through `dotnet run --launch-profile http` and `dotnet run --launch-profile https`.

The final read-only check found no listeners on ports 5270 or 7146 and no lock markers in the managed profile roots. Historical disposable `*.local/test-data/*runtime-*` directories remain outside the real data roots; no user data was selected or deleted.

## Data preservation and safety

- No rebuild or migration of `BrowserProfileManager` was performed.
- SQLite account data, Vault ciphertext, DPAPI/Data Protection keys, existing Chrome profiles, cookies, local storage, and tokens were not accessed or modified.
- No credential was copied into source, logs, reports, tests, or terminal output.
- The account material previously pasted into chat was not read or tested. If it is real, rotate its password and TOTP secret before any future live acceptance.
- CAPTCHA, MFA, passkey, SSO, recovery-code, and anti-bot challenges remain user-controlled; no automated submission or bypass is implemented.

## Live acceptance status

The single-account live flow is prepared but not executed. The application can open the selected managed profile and report visible browser states, but `BrowserOpened` and `BrowserSessionObserved` are not proof of `Authenticated`, `OAuthAuthorized`, or `VerifiedIdentity`.

| Gate | Result |
| --- | --- |
| DI/startup | GO |
| Managed Chrome/Playwright ownership | GO offline |
| Offline headed browser E2E | GO |
| Real ChatGPT login | NOT EXECUTED |
| Real session persistence | NOT EXECUTED |
| Tier 4 overall | PARTIAL |

