# FLASH ROUTER V4.5.6 - Real Provider and Usage/Quota Final Report

Date: 2026-10-19  
Branch: `feat/account-automation-v4-4-recovery`  
HEAD at baseline: `3ddccdf393ed963b32fb081b99ef9ba03d2ad145`  
Classification: `PARTIAL`

## Executive Summary

V4.5.6 hardens the real-provider account observation path and completes the safe Usage & Quota projection path without fabricating values. The implementation now:

- Extracts provider email from official Settings when the account menu does not expose it.
- Preserves stored account email, provider-observed email, plan, display name, and provider MFA as independent evidence.
- Shows `Observed`, `Unavailable`, `Unsupported`, `Failed`, and `Stale` states in Account Inspector.
- Adds an explicit Usage & Quota refresh action for authorized API connections, without reopening ChatGPT Web or replaying browser credentials.
- Serializes Usage & Quota refreshes per account and preserves historical evidence when one connection fails.
- Marks expired provider observations as `Stale` instead of presenting them as current.

No live provider login, credential submission, TOTP submission, authenticated live navigation, or live collection was executed in this phase.

## Baseline and Safety

The worktree was already dirty with V4.5.4/V4.5.5 implementation changes. Those changes were preserved; no reset, clean, destructive migration, commit, push, vault reset, account deletion, or browser-profile deletion was performed.

The following sensitive material was not read or emitted: passwords, TOTP seeds/codes, cookies, tokens, headers, local/session storage, recovery codes, payment data, conversations, or DPAPI keys.

## Implementation Inventory

### Browser observation

`src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs` now reads email after Settings navigation and assigns the correct evidence source. Settings-unavailable states no longer claim that the official Settings surface was successfully observed. Existing bounded navigation, semantic English/Vietnamese lookup, plan-label filtering, MFA parsing, cleanup, and account-owned browser-session rules remain in place.

Synthetic coverage now includes a Settings-only email fixture: the account menu and account popover omit the email, while the official Settings surface exposes it. The observer resolves the email from `OfficialSettingsAccount` and identity verification succeeds only because the stored and provider-observed emails match.

### Account Inspector

`FLASH ROUTER/Components/Console/Components/AccountInspector.razor` now keeps the local recorded plan visibly separate from provider-observed plan evidence. Provider email no longer falls back to the stored account email. Provider plan and MFA values are shown only when their field evidence is `Observed`; unavailable and unsupported states remain explicit. Local credential-flow MFA and provider MFA are labeled separately. The authenticated browser state is shown as `Phiên đã xác thực` where applicable.

### Usage & Quota

`src/Modules/Connections/Application/AccountUsageRefreshService.cs` and `IAccountUsageRefreshPort` provide explicit refresh behavior:

- Manual ChatGPT Web connections are not sent through the API provider orchestrator and remain `Unsupported` when no approved official web quota source exists.
- Authorized API/OAuth connections are checked through the existing provider orchestrator with the existing rate limiting and observation persistence.
- Refreshes are serialized per account to prevent duplicate concurrent writes.
- A failed connection does not prevent other account connections from being checked, and historical snapshots remain available.
- `AccountUsageReader` maps expired or stale observations to `Status = Stale` while keeping nullable unknown values intact.

The Account Inspector exposes a `Làm mới` action and displays source/status information. It does not render zero for unsupported or unknown values.

## Runtime SQLite Certification

Canonical runtime database inspected read-only:

`D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER\.local\data\flashrouter.db`

Observed runtime results:

| Check | Result |
| --- | --- |
| SQLite integrity | `ok` |
| Journal mode | `wal` |
| Account profiles | 3 |
| Browser profile bindings | 8 |
| Provider account observations | 1 |
| Provider observation fields | 5 |
| Required migrations in runtime history | 13 |
| Observation-field uniqueness index | Present |
| Account/provider/observed-time observation index | Present |
| Active-profile binding indexes | Present |

The latest persisted observation is metadata-only in this report: provider `openai-chatgpt`, locale `vi`, authenticated shell detected, account menu detected, status `PartiallyResolved`, failure `ACCOUNT_SETTINGS_UNAVAILABLE`. Its fields contain an observed display name, unavailable provider email with `IDENTITY_EMAIL_NOT_EXPOSED`, unavailable plan, unavailable provider MFA, and unsupported Usage & Quota with `UNSUPPORTED_USAGE_SOURCE`.

No runtime migration was applied.

### Design-time/runtime migration discrepancy

The command below reports every migration as pending:

```powershell
dotnet ef migrations list --project 'FLASH ROUTER/FLASH ROUTER.csproj' --startup-project 'FLASH ROUTER/FLASH ROUTER.csproj' --no-build
```

This is expected from the current design-time factory: `FLASH ROUTER/Persistence/FlashAiDesignTimeDbContextFactory.cs` configures `Data Source=:memory:`. EF compares the migration model against a fresh in-memory database, not against the canonical runtime database. The runtime application uses `SqliteConnectionFactory` and `.local/data/flashrouter.db`; its `__EFMigrationsHistory` contains all 13 migrations, including:

- `20261018100000_AllowArchivedBrowserProfileBindings`
- `20261018150144_AddProviderAccountObservationPersistence`

The discrepancy is therefore a design-time connection-path difference, not evidence that the runtime database is behind. No automatic correction was attempted because the runtime database already has the required history and the safety contract forbids unreviewed production migration changes.

Disposable SQLite fixtures used by the integration suite continue to create and migrate isolated test databases; the runtime database was not used as a test fixture.

## Provider Observation Certification

| Gate | Result |
| --- | --- |
| Authenticated browser state model | PASS in synthetic owned-context fixtures |
| Account-menu observation | PASS in synthetic fixtures |
| Official Settings navigation | PASS for English, Vietnamese, delayed, unavailable, and Settings-only-email fixtures |
| Provider email extraction | PASS; Settings-only email regression covered |
| Identity comparison | PASS for match, mismatch, unavailable, and closed-browser cases |
| Stored email preservation | PASS; provider evidence never overwrites stored identity |
| Display-name provenance | PASS |
| Plan provenance | PASS; promotional text is ignored by parser tests |
| MFA challenge vs configuration | PASS; local/attempt/provider evidence remains separate |
| Candidate/profile promotion safety | PASS in existing V4.5.4/V4.5.5 regression coverage |
| Real provider validation | NOT RUN |

The persisted runtime observation is not treated as new live certification evidence. It remains partial and unverified because the provider email was not exposed in that observation.

## Usage & Quota Certification

| Capability | Result |
| --- | --- |
| Collector/read model | PASS for existing official provider observations |
| Numeric usage and quota parsing | PASS in API observation tests |
| Nullable unknown values | PASS |
| Unsupported ChatGPT Web source | PASS; values remain null and status is `Unsupported` |
| Expired historical observations | PASS; status is `Stale` |
| Provider/source provenance | PASS |
| Explicit refresh operation | PASS for authorized API connection path |
| ChatGPT Web refresh behavior | PASS; no automatic live navigation or credential replay |
| Refresh concurrency | PASS; same-account refresh is rejected while one is running |
| Failure isolation | PASS; one failed API connection does not erase other snapshots |
| SQLite usage history | PASS through existing provider observation persistence |
| Live ChatGPT Web numeric quota | UNSUPPORTED / NOT RUN |

The current runtime ChatGPT Web observation correctly reports Usage & Quota as unsupported. No API Platform usage value is relabeled as ChatGPT Web usage.

## Test Evidence

Commands executed in this worktree:

```powershell
dotnet build 'FLASH ROUTER/FLASH ROUTER.csproj' -c Debug --no-restore
dotnet test tests/FlashAI.IntegrationTests/FlashAI.IntegrationTests.csproj -c Debug --no-restore
dotnet test tests/FlashAI.IntegrationTests/FlashAI.IntegrationTests.csproj -c Release --no-restore
dotnet test tests/FlashAI.ArchitectureTests/FlashAI.ArchitectureTests.csproj -c Debug --no-restore
dotnet test tests/FlashAI.ArchitectureTests/FlashAI.ArchitectureTests.csproj -c Release --no-restore
```

Actual results:

| Suite | Result | Duration |
| --- | ---: | ---: |
| Debug IntegrationTests, full | 240/240 passed, 0 skipped | 3m 30s |
| Release IntegrationTests, full | 240/240 passed, 0 skipped | 3m 48s |
| Debug ArchitectureTests | 11/11 passed | 1s |
| Release ArchitectureTests | 11/11 passed | 655ms |
| Focused provider/usage/refresh tests | 17/17 passed | 8s |
| Focused headed Settings cases | 3/3 passed | 38s |

The full suites include the new Account Usage refresh tests, stale-state test, Settings-only email headed test, existing SQLite persistence tests, browser ownership/promotion tests, and provider observation tests. Builds emitted existing analyzer/style warnings but no compilation or test failures.

## Security and Data Accuracy

- Stored account email remains immutable during provider observation.
- Provider email is displayed only from provider field evidence and never inherited from local account data.
- Local MFA/TOTP-flow evidence is not presented as provider MFA configuration.
- Unsupported, unavailable, and stale usage are not converted to zero or fresh values.
- Account-scoped refresh uses the selected account's connection IDs and per-account concurrency gates.
- No private provider API, hidden network endpoint, token/cookie extraction, or browser storage inspection was added.
- Existing account, vault, profile, migration, and activity data were preserved.

## Final Gate

`Classification: PARTIAL`

Reason: `LIVE_PROVIDER_VALIDATION_NOT_AUTHORIZED`

The safe implementation and synthetic Debug/Release certification are complete. Genuine live-provider certification remains a separate opt-in operation. The current persisted ChatGPT Web record remains `IdentityUnverified`/partial where provider email is unavailable, and numeric ChatGPT Web quota remains unsupported unless an approved visible official source is observed.
