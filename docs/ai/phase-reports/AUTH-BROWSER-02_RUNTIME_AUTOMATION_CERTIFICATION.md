# AUTH-BROWSER-02 Runtime Automation Certification

Date: 2026-10-16
Scope: offline headed Chrome/Playwright certification with a local mock authentication origin.

## Verdict

**GO for offline runtime automation.**

The full host composition builds and resolves the browser-auth services without constructor ambiguity. A real headed Google Chrome process was exercised through Playwright against a disposable local ASP.NET mock site. Synthetic SQLite account records, an encrypted disposable Vault, and a local RFC 6238 TOTP seed were used. No live provider, real account, supplied credential, existing Chrome profile, provider cookie, token, or API was used.

The live-provider and real-account gates are intentionally **NO-GO / NOT EXECUTED** because this certification does not authorize live ChatGPT login or provider access.

## Changes

- src/Modules/Connections/Application/BrowserAuthOrchestrator.cs accepts an optional origin predicate for isolated test composition and continues to default to BrowserOriginPolicy.IsAllowed.
- src/Modules/Connections/Application/BrowserAuthPageStateDetector.cs accepts the same optional predicate and retains the official HTTPS allowlist by default.
- src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs accepts the predicate only on the session object; PlaywrightBrowserAuthenticationSessionFactory still validates and opens only official provider origins in production DI.
- tests/FlashAI.IntegrationTests/BrowserAuthPlaywrightE2ETests.cs adds a headed local-mock suite with account-owned persistent profiles, encrypted synthetic credentials, local TOTP, account isolation, controlled human-action states, invalid credential transitions, and browser-closure handling.
- tests/FlashAI.IntegrationTests/FlashAI.IntegrationTests.csproj references Microsoft.Playwright 1.63.0 for the integration fixture.

The local mock emits a bounded persistent session cookie so the test exercises Chrome profile persistence across context close/reopen. The application never reads that cookie or any browser storage; success is determined only from the visible page state.

## Root Cause Covered

The earlier startup failure was caused by BrowserProfileUiPort exposing both the legacy four-dependency constructor and the canonical IBrowserProfileManager plus IChromeProcessLauncher constructor while DI used implicit constructor selection. src/Modules/Connections/ConnectionsModuleRegistration.cs:78 now uses an explicit factory for the canonical constructor. The compatibility constructor remains available.

The AUTH-BROWSER-02 runtime work did not rebuild BrowserProfileManager, change Vault or SQLite schemas, rotate DPAPI keys, or modify user Chrome profiles.

## Verification Evidence

| Check | Result |
|---|---:|
| DI host composition with ValidateOnBuild and ValidateScopes | 1/1 passed |
| Architecture tests, Debug | 8/8 passed |
| Integration tests, Debug | 119/119 passed |
| Focused headed AUTH-BROWSER-02 tests | 4/4 passed |
| Architecture tests, Release | 8/8 passed |
| Integration tests, Release | 119/119 passed |
| Debug solution build | 0 errors, 0 warnings |
| Release solution build | 0 errors, existing analyzer warnings |
| HTTP launch profile http | Started on http://localhost:5270 |
| HTTP /health/ready and / | 200 / 200 |
| HTTP /_framework/blazor.web.js | 200 |
| HTTP app.css and FLASH_ROUTER.styles.css | 200 / 200 |
| HTTP module CSS bundles | 11/11 returned 200 |
| HTTPS launch profile https | Started on https://localhost:7146 and http://localhost:5270 |
| HTTPS /health/ready and / | 200 / 200 |
| Disposable runtime roots | Created, validated, stopped, and removed |

The Project profiles from FLASH ROUTER/Properties/launchSettings.json were invoked with dotnet run --launch-profile http and dotnet run --launch-profile https. Runtime HTTP evidence was collected from the running host; build success was not used as a substitute.

The focused headed tests covered:

1. Synthetic email, password, and TOTP confirmation flow; visible authenticated state; account-A profile persistence after close/reopen; account-B isolation.
2. CAPTCHA, SSO, and unknown states stopping for human takeover.
3. Invalid password and invalid TOTP transitions without automatic retry.
4. External browser closure mapped to BROWSER_CLOSED.

## Safety Gates

- DI/startup gate: GO.
- Offline headed local-mock gate: GO.
- SQLite/Vault/DPAPI preservation gate: GO for the disposable fixtures; no existing data root was selected.
- Real account/live ChatGPT gate: NO-GO / NOT EXECUTED.
- Provider API, cookies, tokens, and live identity gate: NO-GO / NOT EXECUTED.

## Residual Risks

- Live provider behavior, real CAPTCHA/SSO/passkey challenges, provider session policy, and real-account retention remain unverified by design.
- Release retains existing analyzer warnings; none is a build error or related to browser DI activation.
- Playwright headed coverage requires an installed Google Chrome executable and a desktop session; missing Chrome is reported as a skipped capability rather than silently replaced with headless Chromium.

