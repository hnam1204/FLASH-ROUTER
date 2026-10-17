# CONSOLE-03 - Legacy Hard Removal and Single-Page Console

Date: 2026-10-17
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`
Verdict: `GO` for legacy removal and offline console/runtime acceptance; `PARTIAL` overall because real authenticated ChatGPT login remains intentionally `WAITING FOR USER`.

## Scope and source inventory

- Implemented the CONSOLE-03 prompt directly in the source workspace.
- `docs/ai/requirements/FLASH_ROUTER_AUTOMATION_FIRST_IMPLEMENTATION_MASTER.md` exists and was used as the automation-first requirement copy.
- `FLASH_ROUTER_LEGACY_REMOVAL_SINGLE_PAGE_REBUILD_SPEC.md` was searched for across the workspace and was not found. No substitute specification was created.
- No live credential, provider API, authenticated ChatGPT session, Vault secret, cookie, token, SQLite database, DPAPI key, or existing browser profile was accessed.

## Baseline

Fresh pre-change evidence:

- Debug solution build: `0 warnings / 0 errors`.
- Release solution build: `0 warnings / 0 errors`.
- Architecture tests: `8/8` in Debug and Release.
- Integration tests: `161/161` Debug before source removal.

The old graph was:

`/legacy/accounts` -> `LegacyAccounts.razor` -> `MainWorkspace` -> Header/Filter, account list/detail, account dialogs, connection/subscription/authenticator/reminder/activity tabs.

The module-owned `/accounts` page was a second Account Manager entry point. `AutoLoginConsole` also exposed a footer link back to `/legacy/accounts`.

## Source changes

Removed the legacy route and UI-only graph:

- `FLASH ROUTER/Components/Pages/LegacyAccounts.razor`.
- `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor` and its isolated CSS.
- `FLASH ROUTER/Components/Workspace/Main/BrowserAuthDialog.razor` and its isolated CSS.
- `FLASH ROUTER/Components/Workspace/Header/HeaderBar.razor` and its isolated CSS.
- `FLASH ROUTER/Components/Workspace/Filters/FilterBar.razor` and its isolated CSS.
- `FLASH ROUTER/Components/Layout/NavMenu.razor` and its isolated CSS.
- `src/Modules/Accounts/UI/AccountsPage.razor`, its isolated CSS, and its UI imports.
- Account-only feature UI components and CSS: `AccountDetailPanel`, `AccountInformationTab`, `SecureCopyDialog`, `AccountList`, `AccountListItem`, `CreateAccountDialog`, `ArchiveAccountDialog`, `DeleteAccountDialog`, and `UnsavedChangesDialog`.
- Dead template pages `Counter.razor` and `Weather.razor`.
- The now-unused Accounts feature `_Imports.razor` and host workspace imports.

The removal is source-level, not CSS hiding or redirecting. The deleted set is 18 Razor UI components, 15 isolated CSS files, and 2 dead Razor import files.

Updated active UI composition:

- `FLASH ROUTER/Components/Console/AutoLoginConsole.razor:231-233` no longer contains a legacy Account Manager link.
- `FLASH ROUTER/Components/App.razor:34-46` no longer emits an Accounts UI stylesheet.
- `FLASH ROUTER/Components/ModuleAssemblyCatalog.cs:15-29` no longer discovers the Accounts assembly as a Razor UI assembly.
- `src/Modules/Accounts/README.md:5` documents that Accounts is backend-only until a future product surface is approved.

No Account UI-only DI registration was found. `AddAccountsModule()` remains registered in `FLASH ROUTER/Program.cs` because its contracts, SQLite commands/queries, import/export integration, and persistence contributor remain active.

## Shared infrastructure retained

The following were intentionally preserved:

- `IAccountWorkspaceReader`, `IAccountCommandPort`, `IAccountCatalog`, account contracts, validation, command/query services, model contributor, and SQLite compatibility.
- `SqliteImportExportUiPort` and all account persistence tests.
- BrowserAuthOrchestrator, Playwright-managed Chromium, browser-profile ownership, TOTP, Vault, DPAPI/Data Protection, diagnostics, and host startup.
- `ConnectionsModuleRegistration` and the explicit `BrowserProfileUiPort` DI factory from AUTH-BROWSER-01.
- Existing `.local/data`, `.local/security`, `.local/browser-profiles`, `.local/backups`, SQLite, Vault ciphertext, key rings, backups, and Chrome profiles.

## Regression coverage

`tests/FlashAI.ArchitectureTests/ArchitectureRulesTests.cs:118-137` now asserts:

- No `@page "/legacy/accounts"`.
- No `@page "/accounts"`.
- No legacy route text or Account Manager link in host/module Razor source.
- The legacy page and Accounts UI page do not exist.
- `Home.razor` still renders `AutoLoginConsole`.

The route inventory was updated to exclude the removed Accounts UI route while retaining the other active module routes. The test also distinguishes backend-only Accounts from interactive modules so backend preservation does not require a dead UI page.

`tests/FlashAI.IntegrationTests/AuthBrowser01Tests.cs:98-104` now uses `ConcurrentQueue<string>` for asynchronous progress collection. The first post-change Debug suite exposed a pre-existing callback race in that test; the thread-safe collector fixed the harness issue without changing production behavior.

## Console and security behavior

- `/` remains the only primary product UI and renders `AI Auto Login Console`.
- `EMAIL|PASSWORD|2FA` parsing, escaping, ephemeral credential clearing, bounded sanitized event stream, Start, Cancel, filters, copy, auto-scroll, and Playwright-managed Chromium remain unchanged and covered by existing Console-01/02 tests.
- The final CUA render showed `READY`, package `1.63.0.0`, Chromium revision `1243`, `ms-playwright` cache, the two-column console, and real initial console events (`WaitingForInput`, `CHROMIUM_RESOLVE`). No credential was entered.
- CUA layout measurement at `2560x1440`: `scrollWidth=2560`, `clientWidth=2560`, horizontal overflow `false`, root heading `AI Auto Login Console`, and no legacy text.
- Responsive CSS is preserved in `FLASH ROUTER/Components/Console/AutoLoginConsole.razor.css:141-153`: one column below `1050px`, stacked field/status blocks below `620px`, and two columns above the breakpoint. An exact automated screenshot matrix for every requested viewport was not added; the live measurement and breakpoint audit are the evidence for this phase.
- No before/after pixel diff is claimed because no pre-change screenshot artifact existed in this phase. A post-change screenshot was captured during the disposable host inspection.

## Runtime evidence

Disposable Release host: `http://127.0.0.1:5295` with Preview storage and a disposable `.local/test-data/console-03-health-runtime` root.

- `/health/ready`: `200`.
- `/`: `200`, contains `AI Auto Login Console`, does not contain `/legacy/accounts`.
- `/legacy/accounts`: `404`, empty body; no SPA fallback rendered the old UI.
- `/accounts`: `404`, empty body; the alternate Account Manager route is gone.
- `/_framework/blazor.web.js`: `200`.
- Root stylesheet links: `16/16` returned `200`; no Accounts module stylesheet was emitted.
- `/connections`: `200` was also verified on the earlier disposable host smoke.
- The host was stopped after verification; no listener remains on ports `5270`, `5293`, `5294`, `5295`, `5297`, `5299`, or `7146`.

The disposable hosts created only disposable Data Protection keys under `.local/test-data`; no protected real root was targeted.

## Build and test evidence

Final build:

- Debug solution build: `0 warnings / 0 errors`.
- Release solution build: `0 warnings / 0 errors`.
- A source-touch compilation before the final up-to-date builds surfaced only existing analyzer/test warnings (`CA1822`, `CA1861`, `CA1707`, `CA1305`, `CA1859`, and `xUnit2013`); none were introduced by the legacy removal and none were suppressed.

Final tests:

- Architecture: `9/9` Debug and `9/9` Release, `0` skipped.
- Full integration: `161/161` Debug and `161/161` Release, `0` skipped.
- Headed synthetic Playwright filter: `13/13` Release, `0` skipped.
- Console focused filter: `7/7` Release, `0` skipped.
- Full host DI composition filter: `1/1` Release; `ValidateOnBuild=true` and `ValidateScopes=true` resolve browser/auth services.

## Acceptance matrix

| Requirement | Result |
| --- | --- |
| Legacy route removed | PASS - direct runtime `404` |
| Legacy navigation removed | PASS - footer, NavMenu, workspace imports removed |
| Legacy UI components removed | PASS - source files deleted |
| Legacy CSS cleaned | PASS - isolated CSS deleted; no Accounts CSS emitted |
| Dead frontend code cleaned | PASS - dead template pages and imports removed |
| Shared backend preserved | PASS - Accounts contracts/persistence/import-export retained |
| Single-page AI console | PASS - `/` renders `AutoLoginConsole` |
| Responsive design | PASS for breakpoint contract and live no-overflow measurement; full per-viewport screenshot matrix not automated |
| `EMAIL|PASS|2FA` input | PASS - existing Console-01 tests |
| No mandatory Account creation/Vault for one-shot mode | PASS - existing console flow retained |
| Playwright-managed Chromium | PASS - existing Console-02/runtime evidence |
| Browser profile isolation | PASS offline - existing browser E2E coverage |
| Start/Cancel/realtime console | PASS offline - existing Console and browser E2E coverage |
| No data loss | PASS - no protected roots touched |
| Debug/Release build | PASS |
| Architecture/integration tests | PASS |
| Headed synthetic E2E | PASS `13/13` |
| Real ChatGPT public navigation | VERIFIED in prior AUTH-BROWSER-04..11 evidence; not repeated for this UI-only change |
| Real ChatGPT login | WAITING FOR USER / NOT EXECUTED |
| Real session reopen | WAITING FOR USER / NOT EXECUTED |

## Final status

CONSOLE-03 legacy hard removal and single-page offline/runtime acceptance are `GO`. Overall status is `PARTIAL` only because live authenticated acceptance is intentionally not executed. The previously pasted account values were not used; any live attempt requires rotated credentials, updated local Vault data, and explicit in-app approval.
