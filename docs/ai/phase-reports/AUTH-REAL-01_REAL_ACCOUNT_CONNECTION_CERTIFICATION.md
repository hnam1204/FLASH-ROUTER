# AUTH-REAL-01 Real Account Connection Certification

Date: 2026-10-16  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`

## 1. Summary

AUTH-REAL-01 corrected the actual Integrated account workflow at the SQLite/UI, manual credential, and managed Chrome boundaries. The application now keeps total and filtered account counts distinct, prevents stale list/detail state from crossing an async search, treats manual ChatGPT web credentials as local metadata, and opens Chrome through the account-owned profile after Vault persistence.

This is an offline certification. No real password, TOTP seed, OAuth code/token, cookie, provider session, or live provider usage request was used.

## 2. Baseline

- Solution: 16 `net8.0` projects in the existing solution.
- Existing architecture baseline: `8/8` passed.
- Existing integration baseline before this correction: `108/108` passed according to the handoff evidence.
- The workspace has no Git metadata; existing files and local data were preserved.
- The current Debug apphost may be locked by an existing process; validation used `UseAppHost=false` where required and did not terminate unrelated processes.

## 3. Actual root causes

1. `MainWorkspace.razor` refreshed with the previous `_searchQuery` and selected a newly created record outside the visible result set.
2. `AccountList.razor` rendered `Accounts.Count tài khoản`, so an active search with zero rows looked like an empty database.
3. Manual credential email was persisted as `ExternalIdentity`, then every non-OAuth connection was projected as `ProviderApiCredential` and displayed under verified identity.
4. `SqliteAccountCredentialUiPort` opened `https://chatgpt.com/auth/login` with `Process.Start`, bypassing the managed per-account Chrome profile.
5. UI-created ChatGPT accounts used provider id `openai`, which did not distinguish ChatGPT web from OpenAI Platform API records.

## 4. Reproduced issues

The reported state was reproducible from the code path: an active search could leave the list at zero while the detail panel still showed an account, and a stored manual credential was rendered as `Metadata thủ công · Provider API` with a masked email under `Danh tính đã xác minh`. The browser path was also independent of `BrowserProfileUiPort`.

## 5. Modified files

- `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor`
- `src/Modules/Accounts/Features/BrowseAccounts/UI/AccountList.razor`
- `src/Modules/Accounts/Features/CreateAccount/UI/CreateAccountDialog.razor`
- `src/Modules/Accounts/Features/AccountDetails/UI/AccountInformationTab.razor`
- `src/Modules/Connections/Application/SqliteAccountCredentialUiPort.cs`
- `src/Modules/Connections/Application/AccountConnectionsReader.cs`
- `src/Modules/Connections/Contracts/ProviderUiContracts.cs`
- `src/Modules/Connections/Features/ManageConnections/UI/ConnectionTab.razor`
- `tests/FlashAI.IntegrationTests/UiCAccountConnectionTests.cs`
- `tests/FlashAI.IntegrationTests/UiEAccountConnectionTests.cs`
- Required handoff, runbook, architecture matrix, and this report.

## 6. Search/list/detail fixes

- Added explicit filtered-result count separate from total count.
- Active search/filter displays `N kết quả`; unfiltered view displays `N tài khoản`.
- List refreshes cancel superseded requests and ignore stale results.
- Selection is cleared when it leaves the filtered result set, including detail, observation, mobile-detail, and edit state.
- New account lookup matches both normalized email and provider, then clears search/filter, refreshes from SQLite, and selects the created `AccountId`.
- Existing selection/detail async loads retain version and cancellation guards.

## 7. Manual credential state handling

`ManualWebCredential` is stored in the encrypted Vault and represented in SQLite only by metadata and a Vault reference. The connection remains `CredentialStored` until an independent verification flow exists. The UI uses `Credential web thủ công`, `Người dùng cung cấp`, and `Email đã lưu`; it does not populate `VerifiedIdentity` for manual credentials. The stored account email is read from the account profile for display and is not treated as provider proof.

## 8. Chrome process launch evidence

`SqliteAccountCredentialUiPort` no longer starts a browser. When requested, the workspace calls `BrowserProfileUiPort.OpenAsync(accountId, BrowserOpenTarget.ChatGpt)` after the credential transaction succeeds. The browser port resolves Chrome, validates the managed path, creates or reuses the SQLite binding, launches with `--user-data-dir`, `--new-window`, and `https://chatgpt.com/`, and records launch state. Failure returns a failure status and does not claim that Chrome opened.

## 9. Chrome profile isolation evidence

Synthetic browser tests pass for two distinct accounts, persistent binding lookup after port recreation, same-profile reopening, missing Chrome, launch failure, and traversal rejection. No Default profile, remote debugging, cookie database, local storage, or session token was accessed.

## 10. OAuth protocol and eligibility findings

The existing offline implementation remains aligned with the documented contract: fresh state/nonce/PKCE, exact loopback redirect validation, dynamic first registration, persistent `client_id`, stable `ext_agent_host_id`, scope separation, and ID-token/issuer/audience/expiry/nonce/signature validation. Live registration and authorization remain blocked because application eligibility, registration, consent, and explicit live-test approval are not established for this account-manager application.

## 11. Official sources used

- `https://developers.openai.com/siwc/quickstart`
- `https://developers.openai.com/siwc/token-sharing-open-source`
- `https://developers.openai.com/siwc/token-sharing-open-source/sign-in`
- `https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions`
- `https://developers.openai.com/siwc/token-sharing-open-source/token-reference`
- `https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery`
- `https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations`

## 12. Identity data provenance

Manual form email is `UserProvidedIdentity`. OAuth provider subject/email can become `VerifiedIdentity` only after the official callback and token validation path succeeds. A Vault reference is not a provider identity, a browser launch is not a session, and an account row is not an OAuth authorization.

## 13. Subscription/usage limitations

ChatGPT web plan, subscription, credits, quota, and usage are not inferred from OpenAI Platform API metadata, manual credentials, or an opened browser. Real provider usage remains `NOT EXECUTED`.

## 14. Development vs Release data-root result

The audit preserved the existing configured data roots and did not migrate or overwrite SQLite, Vault, DPAPI/Data Protection keys, or browser data. The existing release/CLI evidence remains separate from live user acceptance; no claim is made that a Debug browser profile is the same as a separately configured release data root without an explicit runtime check.

## 15. SQLite integrity

Existing handoff evidence records SQLite integrity `ok`, application-scoped foreign keys enabled, WAL mode, and migration state up to date after the prior browser-binding migration. This correction adds no migration and performs no reset or destructive cleanup.

## 16. Vault preservation

Credential writes remain atomic with compensating cleanup on SQLite failure. Passwords and TOTP seeds remain Vault-only; no plaintext secret is added to SQLite, logs, UI metadata, or this report.

## 17. Test results

- Debug solution build with the normal apphost: succeeded, `0` warnings, `0` errors after the previously locked process was no longer running.
- Release solution build with `UseAppHost=false`: succeeded, `0` errors; existing analyzer warnings remain.
- Focused credential and browser regression suite after the correction: `10/10` passed, including managed URL and same-profile reopening assertions.
- Full solution test run: architecture `8/8`; integration `109/109`.
- Existing analyzer warnings remain unrelated baseline warnings; no new error was introduced.

## 18. Browser screenshots/evidence

No live browser screenshot is treated as authentication evidence. Synthetic profile/launcher tests are the controlled evidence for this phase. The final local smoke attempt to `http://127.0.0.1:5288/` returned `ERR_CONNECTION_REFUSED` because no application process was listening; no process was started against the real data root. A user-assisted real-session acceptance step remains available and must be performed by the user if they need session-retention confirmation.

## 19. Warnings/errors

- The Debug apphost can be locked by an existing `FLASH_ROUTER.exe`; it was not killed.
- The final normal Debug build passed once no `FLASH_ROUTER.exe` was present; the earlier `MSB3021` is a process file-lock condition, not a source compilation error.
- The local runtime endpoint was not active during the final smoke attempt.
- Existing analyzer warnings remain in boundary/helper/migration code.
- Live provider and OAuth functionality was not invoked.

## 20. Unverified live functionality

Live OAuth eligibility, client registration, consent, callback, token exchange, identity verification, Chrome login completion, session retention, subscription, quota, and provider usage are `BLOCKED` or `NOT EXECUTED`.

## 21. Final verdict

| Gate | Verdict |
|---|---|
| AUTH-REAL-01A Search/list | `GO OFFLINE` |
| AUTH-REAL-01B Metadata truthfulness | `GO OFFLINE` |
| AUTH-REAL-01C Chrome navigation | `GO OFFLINE / USER ACCEPTANCE PENDING` |
| AUTH-REAL-01D Credential workflow | `GO OFFLINE` |
| AUTH-REAL-01E OAuth offline contract | `GO OFFLINE` |
| AUTH-REAL-01E OAuth eligibility | `BLOCKED` |
| AUTH-REAL-01E OAuth live | `NOT EXECUTED` |
| AUTH-REAL-01F Verified account info | `PARTIAL` |
| AUTH-REAL-01G Development/release consistency | `PARTIAL` |
| AUTH-REAL-01H Real data safety | `GO` |
| Chrome session retention | `NOT EXECUTED` |
| Real provider usage | `NOT EXECUTED` |
| Full build/test regression | `GO` |
| Overall AUTH-REAL-01 | `PARTIAL` |

The overall result is intentionally `PARTIAL`: the actual offline defects are corrected, but live provider identity, login/session retention, OAuth eligibility, and usage evidence require user-approved external acceptance and official provider authorization.
