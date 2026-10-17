# AUTH-BROWSER-12 — Transitional Login State Recovery

## Scope

This change addresses a reproducible source-level failure path in the existing CHATGPT browser authentication state machine: after an email/password/OTP submission, the DOM may temporarily contain no recognizable login form. The page classifier returns `Unknown` during this render/redirect interval.

Previously `RefreshUntilEmailTransitionAsync` retried only while `EmailRequired`; `RefreshUntilCredentialTransitionAsync` retried only the previous state. A transient `Unknown` therefore terminated automation before a password or MFA field could become visible.

## Change

- Continue polling while the observation is the previous credential state **or** `Unknown`.
- Preserve the existing five-second bounded wait, 200-ms polling interval, cancellation and origin checks.
- Preserve the fail-closed `Unknown` result when no supported next step is detected before the deadline.
- Reuse the existing Playwright managed-browser implementation and in-process RFC 6238 TOTP generator.
- Do **not** import browser cookies, Bearer tokens, private ChatGPT API requests, or external TOTP services.

## Regression coverage

A synthetic **headed** Playwright test includes a local mock page that briefly removes all authentication inputs for 700 ms between email, password and MFA submissions, then shows the expected next step. The MFA fixture uses localized Vietnamese markup with `autocomplete="one-time-code"`, `name="code"`, `maxlength="6"`, and a submit button labelled “Tiếp tục”.

Expected result: `BrowserSessionObserved` from the local synthetic authenticated page. This does **not** establish successful authentication on the real ChatGPT site.

## Verification

Source changes and modified-file comparison completed. **Build, dotnet test, headed Playwright runtime and real authenticated session: NOT RUN** in the change-authoring environment, which does not have .NET SDK or the local project's Playwright browser installed.

Run from a clean Windows checkout of the proposed branch:

```powershell
dotnet restore '.\FLASH ROUTER.sln'
dotnet build '.\FLASH ROUTER.sln' --no-restore
dotnet test '.\tests\FlashAI.IntegrationTests\FlashAI.IntegrationTests.csproj' --no-build --filter 'FullyQualifiedName~BrowserAuthPlaywrightE2ETests'
dotnet test '.\FLASH ROUTER.sln' --no-build
```

Install the compatible Playwright-managed Chromium revision through this solution's own Playwright installation process if necessary; do not delete any other application's browser cache.

## Scope boundary and security

The provided cURL captures include live session material and should be treated as compromised; revoke exposed sessions before any authorized live acceptance test. The private web endpoints (including `/api/auth/session` and `/backend-api/wham/*`) are not integrated by this change. The unrelated `jlcodes99/cockpit-tools` project is used as an architectural reference only; no code or credentials were copied from it.

## Verdict

**PARTIAL — CODE CHANGE READY FOR LOCAL BUILD AND HEADED E2E VALIDATION.**

No commit was applied to the main branch; all changes are on a separate review branch.
