# FLASH ROUTER V4.5.7.1 - Browser Lifecycle Fix

Date: 2026-10-19
Scope: post-TOTP observation, candidate browser lifecycle, certification routing, and actionable `BROWSER_NOT_OPEN` handling.

## Final verdict

LOCAL FIX VERIFIED. The post-TOTP lifecycle is now bounded and recoverable in synthetic/mock validation. LIVE PROVIDER ACCEPTANCE REMAINS BLOCKED because no authorization was granted to open a real provider account, submit credentials/TOTP, collect fresh provider evidence, or promote a live profile.

## Root cause with evidence

The prior flow did not fail because `TotpSubmitted` was treated as authentication success. It failed because an unresolved post-TOTP observation was treated as a terminal timeout too early:

1. `BrowserAuthOrchestrator.ConfirmAndFillAsync` submitted TOTP and entered `ObservePostLoginAsync`.
2. `ObservePostLoginAsync` retried bounded page reads, but on deadline called `TimeoutAsync`.
3. `TimeoutAsync` removed the attempt, called `DisposeAttemptSessionAsync`, unregistered the managed runtime, disposed the Playwright context, and cleaned the candidate profile.
4. A later certification request therefore saw no runtime registration and returned `BROWSER_NOT_OPEN`.

The source-level race is now covered by tests: a delayed authenticated shell can be observed before disposal, while a lease expiry or browser closure still performs cleanup.

## Changes implemented

- Added `PostLoginPending` to browser/authentication projections. A pending result is not authentication success and never implies identity verification.
- Added a bounded pending candidate lease using the existing attempt expiry boundary, with testable attempt lifetime injection.
- Revoked credential/TOTP automation consent when pending begins. Refresh observation remains possible; repeated password/TOTP submission is rejected.
- Retained an eligible managed session during the pending lease. Temporary sessions still clean up instead of being retained without an observation owner.
- Added deterministic lease expiry cleanup: unregister runtime, dispose context, clean exact candidate profile, persist timeout, and emit safe activity events.
- Added browser-closure cleanup during observation. `BROWSER_CLOSED` no longer leaves the attempt registration alive.
- Added `ActiveAttemptId` to runtime snapshots so certification can distinguish the main browser from an active candidate.
- Extended read-only certification authorization with optional `AuthorizedAttemptId` and `AllowPendingSessionObservation`.
- Certification now permits a pending candidate only when the user authorization and active runtime attempt match. Wrong-account or wrong-attempt contexts fail closed.
- Changed missing-runtime guidance to: `Chưa có phiên trình duyệt đang mở để kiểm tra dữ liệu tài khoản.` The UI points the user to `Mở trình duyệt` or `Đăng nhập lại` and never auto-submits credentials/TOTP.
- Updated Account Inspector and automation progress labels for pending state and candidate lease state.
- Preserved Usage & Quota behavior: ChatGPT Web remains unsupported without a supported official source; no quota is inferred.

## Relevant source locations

- `src/Modules/Connections/Application/BrowserAuthOrchestrator.cs`: `ObservePostLoginAsync`, `TransitionToPostLoginPendingAsync`, `ArmPendingLease`, `TimeoutAsync`, and `HandleSessionFailureAsync`.
- `src/Modules/Connections/Application/AccountBrowserSessionManager.cs`: runtime registration, `ActiveAttemptId` projection, account observation routing, and actionable missing-runtime result.
- `src/Modules/Connections/Application/LiveProviderCertificationCoordinator.cs`: main-session versus candidate scope checks and pending authorization gate.
- `src/Modules/Connections/Contracts/BrowserAuthContracts.cs`: `PostLoginPending` state/status.
- `src/Modules/Connections/Contracts/BrowserProfileContracts.cs`: `BrowserRuntimeSnapshot.ActiveAttemptId`.
- `src/Modules/Connections/Contracts/LiveProviderCertificationContracts.cs`: attempt-scoped read-only authorization.
- `FLASH ROUTER/Components/Console/AutoLoginConsole.razor`: pending UI, authorization scope, and `BROWSER_NOT_OPEN` guidance.
- `FLASH ROUTER/Components/Console/Components/AccountInspector.razor`: pending authentication and candidate lease display.

## Browser and identity state

Before the fix, an unresolved post-TOTP attempt transitioned directly to `TimedOut`, removed its runtime registration, and lost the candidate observation opportunity.

After the fix, the state sequence is:

`MFA_SUBMITTED -> POST_LOGIN_PENDING -> AUTHENTICATED_SESSION_OBSERVED`

or, when evidence never arrives:

`MFA_SUBMITTED -> POST_LOGIN_PENDING -> POST_LOGIN_PENDING_EXPIRED -> cleanup`

The implementation still requires visible official UI evidence. `TotpSubmitted` alone does not create `AuthenticatedSessionConfirmed`, `IdentityVerified`, or a promoted profile.

## Regression coverage

New or updated synthetic coverage includes:

- delayed authenticated shell after TOTP;
- temporary unknown/transient observation followed by success;
- pending state without false success;
- pending lease expiry and cleanup;
- browser context closure during pending observation and cleanup;
- no repeated TOTP after pending;
- no-browser `BROWSER_NOT_OPEN` guidance;
- matching active candidate authorization;
- pending candidate authorization mismatch;
- existing main browser certification path;
- identity mismatch/unverified candidate protection;
- cancellation and profile/session cleanup;
- Usage & Quota and SQLite persistence regression coverage already present in the suite.

## Verification results

- Focused lifecycle/certification/session tests: `37/37` Debug PASS.
- Full IntegrationTests: `257/257` Debug PASS.
- Full IntegrationTests: `257/257` Release PASS.
- ArchitectureTests: `11/11` Debug PASS.
- ArchitectureTests: `11/11` Release PASS.
- Application Debug build: PASS.
- Application Release build: PASS.
- EF `migrations has-pending-model-changes`: PASS; no model changes pending.
- `git diff --check`: PASS; only existing line-ending notices were reported.
- Live-provider tests remain outside ordinary `dotnet test` and were not executed.

## SQLite safety

Read-only runtime inspection identified the effective database as:

`D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER\\.local\\data\\flashrouter.db`

The legacy path `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER\\.local\\flashrouter.db` remains a separate 0-byte file and was not used, deleted, migrated, or replaced.

The effective runtime database had WAL and SHM companions. Read-only SQLite inspection reported:

- `PRAGMA integrity_check`: `ok`;
- journal mode: `wal`;
- `AccountProfiles`: `3`;
- `BrowserProfileBindings`: `8`;
- `ActivityEvents`: `649`;
- `ProviderAccountObservations`: `0`;
- `__EFMigrationsHistory`: `13` rows;
- latest migration: `20261018150144_AddProviderAccountObservationPersistence`.

No canonical DB, WAL, SHM, profile, Vault data, migration, reset, or cleanup command was executed.

## Live authorization status and remaining blockers

No live provider authorization was inferred from previously pasted logs. The following were intentionally not performed:

- opening a real provider account for a new attempt;
- submitting password or TOTP;
- reading fresh provider Settings/account data;
- collecting screenshots or private endpoint data;
- promoting a live candidate profile.

The remaining blocker is live acceptance with explicit, current user authorization. Local lifecycle behavior is verified without weakening the browser profile ownership or identity-promotion gates.
