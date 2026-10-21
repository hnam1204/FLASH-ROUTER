# FLASH ROUTER V4.5.4 - Identity and Profile Promotion Final Report

Date: 2026-10-18  
Branch: `feat/account-automation-v4-4-recovery`  
Baseline HEAD: `3ddccdf393ed963b32fb081b99ef9ba03d2ad145`  
Working tree: intentionally uncommitted; existing user changes were preserved.

## 1. Final Status

**PARTIAL**.

The repository implementation, SQLite migrations, synthetic browser flows, headed local-mock Playwright flows, identity policy, candidate promotion policy, UI state semantics, and Debug/Release regression suites are green. Live ChatGPT authentication and live-provider identity certification were not run because no live credentials, TOTP, cookies, tokens, or provider session data were authorized for use.

The acceptance invariant is satisfied offline: an authenticated browser session with unavailable or unverified identity remains distinct from an identity-verified account, and an unverified candidate cannot replace the account-bound profile.

## 2. Root Causes Identified

1. Post-login observation used one fatal `Locator("body")` read with a three-second timeout. Navigation and transient DOM readiness were not represented as retryable observation outcomes.
2. The authenticated ChatGPT shell could expose only an account/profile control until its menu was opened. The previous detector therefore missed an authenticated shell and could not obtain a provider email.
3. Display names, avatars, plan labels, and authenticated-shell markers were not sufficient identity evidence. Email evidence needed to come from the visible provider account UI and be compared to the stored email.
4. Candidate promotion archived the previous binding and then inserted the new binding, but the old unique index also covered archived rows. SQLite rejected the insert.
5. Every live activity event reloaded the complete account inspector projection, including connections, activity, browser status, and usage reads.
6. The UI labels did not clearly separate browser-session confirmation, provider identity verification, MFA-step observation, MFA configuration, and candidate promotion.
7. The account deletion path is explicit in `SqliteAccountCommandPort.DeleteAccountAsync`; authentication failure, timeout, candidate cleanup, and projection refresh paths do not call it. New regressions assert account preservation on those paths.

## 3. Implementation Changes

### Browser observation and identity

- `src/Modules/Connections/Application/PlaywrightBrowserAuthenticationSession.cs`: bounded body-DOM retries with cancellation and an overall budget; safe codes for timeout, navigation-in-progress, transient DOM failure, and closed browser/page; account-menu authenticated-shell detection; provider account-popover email observation without harvesting full page content.
- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs`: observation failure code/message fields and explicit authentication/identity states.
- `src/Modules/Connections/Application/BrowserAuthOrchestrator.cs`: retries observation failures without resubmitting email, password, or TOTP; records identity and promotion transitions; keeps fail-closed behavior for inconclusive observation.

### Candidate promotion and persistence

- `src/Modules/Connections/Application/BrowserProfileManager.cs`: promotion requires `IdentityVerificationStatus.IdentityVerified`, validates account/provider/path metadata, keeps the old binding archived, and performs the binding change transactionally.
- `src/Modules/Connections/Application/EphemeralBrowserProfileFactory.cs`: exact generated candidate directory cleanup on non-promoted terminal paths; retained candidates remain available only for explicit promotion or pending review.
- `src/Modules/Connections/Contracts/BrowserProfileContracts.cs`: promotion API carries the identity verification status.
- `src/Modules/Connections/Persistence/ConnectionModelContributor.cs`: active-binding uniqueness excludes archived bindings.
- `FLASH ROUTER/Persistence/Migrations/20261018100000_AllowArchivedBrowserProfileBindings.cs`: drops the old SQLite index and recreates it as a partial unique index with `State <> 'Archived'`.
- `FLASH ROUTER/Persistence/Migrations/FlashAiDbContextModelSnapshot.cs`: records the filtered index in the EF model snapshot.

### UI and tests

- `FLASH ROUTER/Components/Console/AutoLoginConsole.razor`: identity-scoped KPI semantics, bounded live activity updates, durable projection refresh only for state-changing events, and promotion event labels.
- `FLASH ROUTER/Components/Console/Components/AccountInspector.razor`: explicit identity state, MFA-step observation, MFA configuration uncertainty, usage-source limitations, and candidate status.
- `FLASH ROUTER/Components/Console/Components/KpiCardsSection.razor`: labels distinguish confirmed browser sessions from identity verification work.
- `FLASH ROUTER/Components/Console/AutoLoginConsole.razor.css`: keeps the console log readable in both themes and corrects compact/technical-detail columns.
- `tests/FlashAI.IntegrationTests/AuthBrowser01Tests.cs`: delayed-navigation, transient DOM, timeout, cleanup, cancellation, origin rejection, human-takeover, and account-preservation coverage.
- `tests/FlashAI.IntegrationTests/ProviderAccountVerificationTests.cs`: verified match, mismatch, unavailable email, display-name-only evidence, and ambiguous alias coverage.
- `tests/FlashAI.IntegrationTests/BrowserProfilePromotionTests.cs`: blocked unverified promotion, committed verified promotion, archived-binding persistence, and manager-restart recovery.

## 4. Authentication State Transition

```text
NotStarted
  -> BrowserStarting -> Navigating -> ObservingPostLogin
  -> GuestLandingLoginAvailable -> EmailRequired -> PasswordRequired
  -> MfaRequired -> ObservingPostLogin
  -> BrowserSessionObserved / AuthenticatedSessionConfirmed
       -> IdentityVerified
            -> PromotionApproved -> PromotionCommitted
       -> IdentityUnverified / IdentityMismatch
            -> PromotionBlocked
       -> observation retryable failure
            -> Unknown + bounded retry, without credential resubmission
       -> browser closed / timeout / cancellation / auth rejection
            -> terminal failure state; original account/profile preserved
```

`BrowserSessionObserved` proves only the current provider UI session. `IdentityVerified` additionally requires comparable provider-observed email evidence equal to the selected account email after the documented normalization policy.

## 5. Identity Evidence and Promotion Policy

- Accepted provider evidence is a comparable email read from the official, visible ChatGPT account/profile UI or its visible account popover.
- Stored account email and observed provider email are normalized case-insensitively with surrounding whitespace removed; aliases such as `selected+alias@example.test` are not assumed equivalent.
- Display name, avatar, `Free` plan, authenticated shell, or conversation text never establishes email ownership.
- No private endpoints, cookies, tokens, complete storage, password, TOTP seed, or TOTP code is read or persisted.
- If no comparable email is available, the result remains a confirmed browser session with `IdentityUnverified`; the candidate may remain pending but cannot replace the original binding.
- A definitive different email produces `IdentityMismatch` and blocks promotion.
- Promotion is allowed only for `IdentityVerified`, and the partial unique index preserves archived binding history while allowing one active binding.

## 6. Playwright Reliability Results

Focused local-mock headed Playwright result:

```text
BrowserAuthPlaywrightE2ETests: 15 passed, 0 failed, 0 skipped
```

The full synthetic suite covers authenticated session persistence, guest landing, delayed post-MFA navigation, transient DOM observation, multi-page context behavior, browser closure, identity match/mismatch/unavailable evidence, and account/profile isolation. The synthetic timeout and retry tests also verify that email/password/TOTP are each submitted once.

No result in this report is live-provider certification.

## 7. EF Core Refresh Query Measurement

Workload definition: one selected account, one inspector load, no provider connection rows, and one intermediate activity event. The count below is a deterministic EF command-shape measurement from the production projection methods, not a wall-clock benchmark and not a claim about a live database trace.

| Path | Before | After |
|---|---:|---:|
| Initial selected-account inspector load | 14 SQL commands in the empty-connection case | 14 SQL commands; unchanged and still bounded |
| One intermediate live activity event | 14 additional commands because the complete inspector was reloaded | 0 additional EF commands; event is appended to the in-memory bounded list |
| One durable state-changing event | 14 additional commands | 3 commands: account detail plus browser binding/authentication projection |

For an account with one provider connection, the previous full event refresh also added the connection/observation/sync reads from `AccountUsageReader`; those reads are no longer triggered by intermediate activity events. A runtime EF command interceptor benchmark was not added, so this section is explicitly a reproducible query-budget audit rather than a production latency certification.

## 8. Build and Test Results

Commands:

```text
dotnet build ".\\FLASH ROUTER\\FLASH ROUTER.csproj" -c Debug --no-restore
dotnet build ".\\FLASH ROUTER\\FLASH ROUTER.csproj" -c Release --no-restore
dotnet test ".\\tests\\FlashAI.IntegrationTests\\FlashAI.IntegrationTests.csproj" -c Debug --no-build --no-restore
dotnet test ".\\tests\\FlashAI.IntegrationTests\\FlashAI.IntegrationTests.csproj" -c Release --no-restore
```

Results:

- Debug application build: succeeded, 0 errors.
- Release application build: succeeded, 0 errors; existing analyzer warnings remain.
- Debug integration suite: 224 passed, 0 failed, 0 skipped.
- Release integration suite: 224 passed, 0 failed, 0 skipped.
- Focused promotion/identity/retry group: 27 passed, 0 failed, 0 skipped.
- Focused SQLite/projection/integrity group: 11 passed, 0 failed, 0 skipped.
- Focused UI source group: 18 passed, 0 failed, 0 skipped.
- `dotnet ef migrations list` includes `20261018100000_AllowArchivedBrowserProfileBindings`.
- `dotnet ef migrations has-pending-model-changes` reports no model changes.

## 9. Database, Vault, and Profile Integrity

- `CanonicalDatabaseIntegrityTests` passed in both full configurations: SQLite `PRAGMA integrity_check` returned `ok`, and `PRAGMA foreign_key_check` returned no rows.
- The canonical database was accessed read-only by the integrity test; no account or production row was deleted or recreated.
- Disposable SQLite fixtures were used for promotion and persistence tests.
- Vault tests use synthetic encrypted credentials and do not print or inspect real secrets.
- Candidate profile tests use disposable directories. Verified promotion preserves the original directory and survives `BrowserProfileManager` recreation.
- The migration script was inspected and contains the expected `DROP INDEX`, filtered unique index, and migration-history insert.

## 10. Account Deletion and Recreation Audit

The only account-row deletion path found in the integrated account command surface is the explicit `DeleteAccountAsync` command invoked by the confirmed delete UI. Authentication orchestration, observation retry, timeout handling, candidate cleanup, browser-session unregister, and live activity refresh do not call that command or remove `AccountRecord` rows.

Regression assertions now verify that timeout, failed candidate cleanup, origin rejection, and human takeover leave the selected account present. The existing durable account lifecycle test separately verifies explicit archive/restore/delete behavior.

## 11. Remaining Blockers

- Live ChatGPT login and provider email observation were not run; therefore current provider DOM behavior and live identity equality remain un-certified.
- No live credentials, TOTP, cookies, browser storage, or private provider endpoints were used.
- Visual Studio restart, extension-isolation, and system-wide IDE/SDK update are outside this repository validation pass; the repository was tested with the installed .NET SDK and ASP.NET Core 8 target.
- The EF query section is a deterministic command-budget audit, not a runtime latency benchmark with an interceptor trace.
- Existing analyzer/style warnings remain; they do not fail the build or tests.

## 12. Verdict

**PARTIAL - implementation and offline/synthetic acceptance passed; live-provider certification intentionally not performed.**

The required safety rule is met: a confirmed browser session with unverified identity remains visibly and persistently distinct from an identity-verified account, and profile promotion is fail-closed until provider identity equality is verified.
