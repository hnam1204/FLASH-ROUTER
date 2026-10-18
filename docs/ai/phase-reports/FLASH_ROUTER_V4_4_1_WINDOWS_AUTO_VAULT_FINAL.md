# FLASH ROUTER V4.4.1 Windows Automatic Vault Final Report

Run date: 2026-10-18

Evidence rule: claims below are limited to the current worktree and current validation runs. Synthetic browser results are not live-provider results.

## 1. Executive Summary

V4.4.1 removes routine browser-login dependence on the scoped Vault unlock flag while retaining encrypted credential storage. Compatible credentials are protected with ASP.NET Core Data Protection and Windows DPAPI `CurrentUser`, read automatically, and classified safely when unavailable. Focused automatic-access tests passed 41/41. Architecture tests passed 11/11 in Debug and Release. Full integration suites passed 195/198 in both configurations; the three failures are documented in Section 25.

## 2. Repository/worktree baseline

The authoritative worktree is `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`, branch `feat/account-automation-v4-4-recovery`, HEAD `8a115c72360e5e2bd9bb956d347dfd726a3bb6fc`. The solution contains nine projects. The worktree was already dirty with V4.2-V4.4 UI, activity, scheduler, persistence, and automation changes; those changes were preserved. No reset, stash, commit, push, merge, or unrelated worktree deletion was performed.

## 3. Actual root cause of WAITINGFORVAULTUNLOCK

`FileSecretVault` already used ASP.NET Core Data Protection and a Windows-user-protected key ring. The routine interruption was caused by consumers consulting scoped `IVaultAccessState`, whose `IsUnlocked` value represented an authenticator-session flag rather than cryptographic availability. The compatible credential could be decrypted without that flag, but the browser path treated the flag as a prerequisite and surfaced the unlock state.

## 4. Existing Vault security architecture

`FileSecretVault` stores encrypted envelopes outside SQLite. Each entry uses a logical-key-derived purpose, authenticated Data Protection, atomic replacement, directory locking, corruption checks, reparse-point checks, and safe error translation. The key ring is persisted by `DataProtectionSetup` and protected with DPAPI for the current Windows user.

## 5. Selected Windows protection strategy

The lowest-risk strategy is to preserve the existing Data Protection design and remove only the incorrect routine unlock gate. The selected scheme is `AspNetCoreDataProtection-DPAPI-CurrentUser`, with `WindowsCurrentUser` scope and format version `1`. No plaintext fallback, hardcoded secret, machine-wide key protection, or key regeneration was introduced.

## 6. Security trade-offs

Windows-user-bound protection gives automatic authorized access and isolates secrets from other Windows users, but it is not protection from malware or a fully compromised process running as the same user. The design is also intentionally not portable across arbitrary machines or replaced Windows profiles. Those limits are documented in ADR-0008.

## 7. Updated credential protection contract

The existing vault and credential-reference contracts were retained. New references now persist protection scheme and format version alongside account/provider association, credential kind, vault key, scope, timestamps, and stable connection identity. Secret material remains in the vault payload, not in the reference record.

## 8. Automatic credential retrieval implementation

`SqliteAccountCredentialUiPort` protects new and replaced ChatGPT credentials before committing the SQLite reference. `BrowserAuthCredentialReader` verifies account ownership, provider, metadata compatibility, and payload validity, then reads the vault without consulting `IVaultAccessState`. Sensitive byte arrays are zeroed after parsing.

## 9. Legacy credential migration strategy

References whose scheme or format does not match the current scheme return `LEGACY_UNLOCK_REQUIRED`. Authorized migration must decrypt in memory, protect under the current Windows-bound scheme, verify a round trip, update SQLite safely, and retain the original encrypted representation until success. Unrecoverable legacy material is preserved and requires replacement.

## 10. Actual migration results

No live legacy secret was migrated because no authorized legacy password or migration fixture was supplied. The current run verifies the safe negative path: changing a persisted reference to an incompatible format produces `LEGACY_UNLOCK_REQUIRED` and never returns the credential. No legacy data was deleted.

## 11. Vault state machine changes

Browser authentication no longer transitions through `WaitingForVaultUnlock` for compatible credentials. Readiness now distinguishes missing references, provider mismatch, legacy metadata, unavailable protection keys, unauthorized access, corrupted payloads, invalid ownership, and storage failures. The authenticator feature may still expose its separate legacy unlock API for authenticator-managed data; that API is not a prerequisite for compatible browser credentials.

## 12. Removed or retained unlock UI

The mounted `VaultUnlockDialog`, authenticator injection, pending-unlock state, and browser-login resume handlers were removed from the account console flow. The authenticator tab retains its own legacy TOTP management UI and unlock contract because it is a separate feature boundary. Browser credential errors now show replacement or accurate protection-failure guidance.

## 13. AccountRegistrationCoordinator changes

No `AccountRegistrationCoordinator` type exists in the authoritative nine-project source. Registration is coordinated by `AutoLoginConsole.HandleCreateAccount`: create the account, resolve its persisted ID, save the encrypted credential, refresh the account view, and invoke the browser authentication workflow. The Add Account dialog sets `OpenBrowserAfterSave = true`, and the console starts Auto Login after successful credential persistence.

## 14. BrowserAuthOrchestrator changes

`BrowserAuthOrchestrator` now relies on credential readiness and automatic vault access rather than the scoped unlock flag. It creates one account-scoped Auto Login gate, opens the managed profile, executes supported email/password/TOTP steps, records session observations, cleans up browser resources, and returns `AUTO_LOGIN_IN_PROGRESS` for a duplicate account attempt.

## 15. AutoStart evidence

The Add Account UI action is labeled `Lưu và tự động đăng nhập`. Current source evidence shows the console calls `CredentialPort.SaveAsync`, refreshes data, and invokes `ExecuteLoginAgain`, which calls `BrowserAuth.AutoLoginAsync` with a progress stream. Synthetic Auto Login coverage passed in `AuthBrowser01Tests`.

## 16. Restart persistence evidence

`FileSecretVaultRoundTripsAfterProviderAndVaultRestart` passed. It writes with one Data Protection provider instance, disposes the vault and provider, recreates both against the same key ring and vault directory, and reads the original value. The account-level readiness and credential reader tests also passed after the automatic-access change.

## 17. Windows identity compatibility

The configured protection context is current-user DPAPI. Same-user automatic access is covered by the passing key-ring and restart tests. Different Windows User B, service identity, elevation, and profile migration were not executed in this run and remain limitations rather than certified claims.

## 18. SQLite integrity results

The canonical SQLite integrity and foreign-key checks are included in the passing integration run. Credential metadata migration `20261018000000_AddCredentialProtectionMetadata` adds only `ProtectionScheme` and `ProtectionFormatVersion`; no plaintext password or TOTP columns were added.

## 19. Activity Journal compatibility

The durable activity journal implementation and `ActivityJournalTests` ran within the full integration suite. Credential and browser events use sanitized summaries and safe categories; secret values are not written to audit details.

## 20. Usage scheduler regression

`ProviderSyncSchedulerTests` ran within the full integration suite. The scheduler continues to default to sixty-second ticks, avoids billable usage reads unless explicitly allowed, persists next-eligible state, and applies rate-limit backoff.

## 21. Playwright profile lifecycle tests

The headed Playwright suite exercised managed profile ownership, guest landing detection, MFA transitions, invalid credentials, browser closure, profile isolation, and cleanup. Most profile lifecycle cases passed, but two persistence assertions failed after the synthetic login session was reopened: one observed `EmailRequired` and one observed `GuestLandingLoginAvailable` instead of `Authenticated`. These failures are not hidden or reclassified as success.

## 22. Debug build

The isolated Debug test build completed successfully while running the focused and full suites. Existing analyzer warnings remain; no compile errors were reported. The focused automatic-access suite passed 41/41.

## 23. Release build

The isolated Release solution build completed successfully with 0 errors and 99 existing analyzer warnings. Release architecture tests passed 11/11. Release integration tests were executed against the isolated Release output.

## 24. Architecture tests

Debug architecture tests: 11 passed, 0 failed. Release architecture tests: 11 passed, 0 failed. The nine-project solution list remained unchanged.

## 25. Integration tests

Debug integration: 195 passed, 3 failed, 0 skipped, 198 total. Release integration: 195 passed, 3 failed, 0 skipped, 198 total. The failures were one CLI workspace-root assertion caused by isolated output changing `AppContext.BaseDirectory`, plus the two headed Playwright persistence assertions described in Section 21. The focused vault/authentication suite passed 41/41.

## 26. Headed E2E evidence

Synthetic headed E2E was executed with a local mock authentication server and no live provider account. The tests confirmed supported email/password/TOTP transitions and failure classifications in the passing cases. Because two persistence assertions failed, headed E2E is not certified as full pass. No live-provider login or provider identity verification was attempted.

## 27. Security findings

No new critical security finding was identified in the reviewed V4.4.1 path. Positive evidence includes encrypted vault files, wrong-key rejection, corruption rejection, key-bound ciphertext, restart persistence, metadata-only SQLite references, redacted diagnostics, and duplicate-attempt suppression. The same-user malware limitation and non-portability remain accepted design constraints.

## 28. Remaining limitations

Live provider login, provider identity verification, different-Windows-user isolation, service identity behavior, elevated/non-elevated comparison, profile migration, and authorized legacy migration were not certified. Headed Playwright profile persistence still needs investigation. The CLI test is sensitive to isolated output layout and should be made path-independent before claiming a completely green full suite.

## 29. Rollback instructions

Rollback requires restoring the reviewed V4.4 worktree snapshot or reverting the specific V4.4.1 source and migration changes through normal version-control review. Do not delete vault files or regenerate keys. Preserve the existing DPAPI-protected key ring and encrypted legacy material. Before any rollback affecting the database, make a verified SQLite backup and confirm migration compatibility.

## 30. Final verdict

Automatic Credential Access: PASS for compatible credentials under the same authorized Windows user, supported by 41/41 focused tests. Synthetic Browser Login: PASS for the deterministic synthetic orchestration path; headed E2E: NOT FULL PASS because two profile-persistence assertions failed. Live Provider Login: NOT CERTIFIED. Provider Identity Verification: NOT CERTIFIED. Overall V4.4.1 acceptance: PARTIAL PASS, not FULL PASS, pending headed profile persistence and the documented unexecuted Windows/live-provider gates.
