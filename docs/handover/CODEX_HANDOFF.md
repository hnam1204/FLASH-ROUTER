# TÀI LIỆU BÀN GIAO KIẾN TRÚC & HỢP ĐỒNG GIAO DIỆN (UI-B HANDOFF FOR CODEX BACKEND)

## CONSOLE-04 UI Rebuild & Runtime Certification (2026-10-17)

- **AI Auto Login Console (`/`):** Full visual rebuild according to Dark Developer Dashboard reference language. Retains strictly ONE product interface (`/`), with AI platform selector, fast `EMAIL|PASS|2FA` quick input, session mode (Temporary / Keep session), primary Start, Cancel, fresh-consent Retry, truth-bound Chromium runtime card with collapsible technical specs, live console toolbar with severity filters (ALL/INFO/PASS/WARN/ERROR), auto-scroll, copy sanitized logs, clear logs, and compact terminal outcome strip.
- **Design Tokens:** Implemented centralized semantic tokens in `foundation/tokens.css` for Dark Mode (Default: `--app-bg: #081320; --app-surface: #101E30; --app-border: #25364A; --accent-primary: #2681FF; --accent-teal: #16C5AB`) and Light Mode (`--app-bg: #EFF3F9; --app-surface: #FFFFFF; --app-border: #D5DEEB; --app-console: #102033; --accent-primary: #176CE3`).
- **Dark/Light Theme Toggle:** Accessible header toggle button with instantaneous switching without reload, without input resetting, and without disrupting running Playwright attempts. Preference persisted in `localStorage['flashrouter_theme']` with zero secret leakage. Pre-paint `<head>` bootstrap script prevents flash of wrong theme on reload.
- **Typography & Responsive Layout:** Sans-serif typography (`Be Vietnam Pro`, `Inter`, `Segoe UI`), Cascadia Code/Consolas monospace for terminal, 24px heading, 17px section headers, 13.5px labels, 12.5px console logs, 46px primary button, zero serif fonts. Full-width desktop grid (1600px max) with 360-420px control panel and flexible terminal console. Stacked 1-column layout on mobile/tablet without horizontal overflow.
- **Visual Evidence Matrix:** 14 full-viewport screenshot artifacts and webp video session recording in artifacts directory covering `375x812`, `768x1024`, `1024x768`, `1280x800`, `1440x900`, `1920x1080` in both Dark and Light modes.
- **Regression:** Solution builds with `0 errors` in Debug and Release; Architecture tests `9/9 passed`, Integration tests `167/167 passed` (including 6 new targeted CONSOLE-04 tests and Playwright headed browser tests).
- **Data Preservation:** User data roots (`.local/data`, `.local/security`, `.local/browser-profiles`, `.local/backups`, `.local/flashrouter.db`) remained 100% untouched. Disposable test data roots cleaned up cleanly.
- **Detailed Report:** `docs/ai/phase-reports/CONSOLE-04_ANTIGRAVITY_DARK_LIGHT_UI_REBUILD.md`.
- **Live Authentication Status:** Offline engine and runtime GO; real ChatGPT live authentication remains `WAITING FOR USER` for operator-entered rotated credentials.

## LIVE-ACCEPTANCE-03 Final Certification (2026-10-16)

- Tier 4 controlled browser workflow is GO offline and `PARTIAL` overall. Disposable host `http://127.0.0.1:5290` returned health/root/Blazor/CSS/SignalR success and a Blazor WebSocket `101`; the host was stopped cleanly.
- Headed Playwright E2E passed with a local mock origin, synthetic accounts, disposable Vault data, persistent profile fixtures, Account A/B isolation, human takeover, invalid credential transitions, and browser closure. This is not evidence of a real ChatGPT login.
- Real ChatGPT URL navigation, Vault unlock for a real account, password/TOTP submission, real session persistence, cookies/tokens, and the previously pasted credential were not used. Real login and real persistence are `NOT EXECUTED`.
- Tier 5 provider pipeline is GO offline and `PARTIAL` overall. OpenRouter standard-key capability resolution remains permission-aware; no provider URL was called and live request count is `0`. Live status is `READY / NOT EXECUTED`.
- Fresh regression evidence: Debug and Release solution builds `0 warnings / 0 errors`, architecture tests `8/8`, and integration tests `127/127`.
- Real SQLite, Vault, DPAPI/Data Protection keys, existing accounts/connections/observations, and Chrome profiles were not modified. Disposable roots remain under `.local/test-data`.
- Reports: `docs/ai/phase-reports/LIVE-ACCEPTANCE-03_CHATGPT_BROWSER_VERIFICATION.md` and `docs/ai/phase-reports/LIVE-ACCEPTANCE-03_PROVIDER_USAGE_VERIFICATION.md`.
- Next operator action requires selecting one account or one provider connection inside FLASH ROUTER and approving that exact live action in the UI; the pasted credential is not authorization.

## LIVE-ACCEPTANCE-02 Final Certification (2026-10-16)

- The current source baseline remains intact; no Tier 6 distribution, GitHub, installer, PATH, commit, push, or deployment work was performed.
- Disposable Integrated host preflight passed on `http://127.0.0.1:5290`: health `200`, root `200`, Blazor script `200`, 17 discovered CSS links `200`, SignalR negotiate `200`, and Blazor circuit/WebSocket `101`. The host was stopped with Ctrl+C and no listener remained on 5290.
- Disposable browser verification showed the real account workspace, opened and closed the `Thêm tài khoản ChatGPT` dialog, and did not create an account or enter sensitive data.
- Offline browser ownership and headed Playwright remain GO. Real ChatGPT login, real MFA, real profile retention, and live provider API requests remain `NOT EXECUTED`.
- Current regression: Debug and Release architecture `8/8`, integration `127/127`, and solution builds `0 warnings / 0 errors`.
- Tier 4 verdict: `PARTIAL` (`LA-02A/B/C/E` offline GO; `LA-02D` real login and real website persistence NOT EXECUTED). Tier 5 verdict: `PARTIAL` (`LU-02A/D` offline GO; `LU-02B` READY/NOT EXECUTED; `LU-02C` automatic ChatGPT Web data UNSUPPORTED).
- Existing `.local/data`, `.local/security`, `.local/browser-profiles`, `.local/backups`, SQLite records, Vault, DPAPI keys, and user browser data were not modified. Disposable runtime roots remain under `.local/test-data` as test artifacts only.
- Reports: `docs/ai/phase-reports/LIVE-ACCEPTANCE-02_CHATGPT_LOGIN_CERTIFICATION.md` and `docs/ai/phase-reports/LIVE-ACCEPTANCE-02_PROVIDER_USAGE_CERTIFICATION.md`.

## LIVE-AUTH-01 + LIVE-USAGE-01 Final Certification (2026-10-16)

- Tier 4 is `PARTIAL`: managed Chrome/Playwright ownership, offline headed browser E2E, DI startup, runtime host, profile isolation, and diagnostics are GO; real ChatGPT login and real session-retention acceptance are `NOT EXECUTED`.
- Tier 5 is `PARTIAL`: offline provider capability modeling and read-only observation behavior are GO; live provider usage, quota, credits, billing, and ChatGPT web entitlement are `NOT EXECUTED`.
- The exact historical live profile failure was not reproduced against an active user profile. Read-only inspection found no active lock markers or Chrome process for the managed roots. The confirmed deterministic defect was missing shared profile ownership, stale-lock misclassification, and generic launch diagnostics.
- `src/Modules/Connections/ConnectionsModuleRegistration.cs:78-83` explicitly constructs `BrowserProfileUiPort` with `IBrowserProfileManager` and `IChromeProcessLauncher`; `src/Modules/Connections/Application/BrowserProfileUiPort.cs:14-38` keeps the legacy constructor for compatibility. Constructor ambiguity is no longer present.
- `src/Modules/Connections/Application/BrowserProfileOwnership.cs:6-145` coordinates managed-Chrome and Playwright leases; `ChromeProcessLauncher.cs:61-148` and `PlaywrightBrowserAuthenticationSession.cs:20-166` release ownership on failure, cancellation, early exit, and disposal.
- Regression evidence after the final implementation: Debug and Release architecture `8/8`, integration `127/127`, and solution builds `0 warnings / 0 errors` in both configurations.
- Runtime evidence: HTTP launch profile `http://localhost:5270` and HTTPS launch profile `https://localhost:7146` served health, `/`, Blazor assets, host CSS, and the module CSS checks with HTTP 200. Final inspection found no listeners on 5270/7146 and no lock markers in managed roots.
- SQLite, Vault ciphertext, DPAPI/Data Protection keys, existing accounts, existing Chrome profiles, cookies, tokens, and provider data were not accessed or modified. The previously pasted credential was not used; if real, rotate its password and TOTP secret.
- Historical disposable runtime fixture directories remain under `.local/test-data` only; they are not the real data roots. No cleanup command was run against user data.
- Detailed reports: `docs/ai/phase-reports/LIVE-AUTH-01_CHROME_ACCOUNT_ACCEPTANCE.md` and `docs/ai/phase-reports/LIVE-USAGE-01_PROVIDER_DATA_ACCEPTANCE.md`.

## AUTH-BROWSER-02 Runtime Automation Certification (2026-10-16)

- Offline headed browser certification is GO. BrowserAuthPlaywrightE2ETests uses a local mock ASP.NET authentication site, disposable SQLite/Vault roots, synthetic accounts, and local RFC 6238 TOTP only.
- Four headed Chrome scenarios pass: account-owned profile persistence and isolation, CAPTCHA/SSO/unknown human takeover, invalid password/TOTP transitions, and external browser closure.
- Regression evidence: Debug architecture 8/8, Debug integration 119/119, Release architecture 8/8, Release integration 119/119; Debug build 0/0, Release build 0 errors with existing analyzer warnings.
- Disposable HTTP and HTTPS launch-profile hosts started successfully. /health/ready, /, Blazor assets, app CSS, and all 11 module CSS bundles returned HTTP 200. Disposable runtime roots were removed after shutdown.
- The production Playwright factory remains official-origin-only. The local origin predicate exists only for direct synthetic test composition and does not alter production DI registration.
- No supplied credential, live ChatGPT login, provider API, existing Chrome profile, cookie, token, SQLite database, Vault, or DPAPI key ring was used. Live-provider and real-account acceptance remain NO-GO / NOT EXECUTED.
- Detailed evidence: docs/ai/phase-reports/AUTH-BROWSER-02_RUNTIME_AUTOMATION_CERTIFICATION.md.

## AUTH-REAL-01 Account Workflow Correction (2026-10-16)

- Fixed the Integrated workspace refresh race: list requests now cancel superseded searches, filtered count is separate from total count, and selection/detail are cleared when the selected account leaves the active result set.
- After account creation, the UI finds the new record by email and provider, clears search/filter state, refreshes SQLite, and selects the same `AccountId` shown in the list. UI-created ChatGPT web accounts use provider id `openai-chatgpt`, distinct from OpenAI Platform API records.
- Manual web credentials remain `CredentialStored` until a user completes the official website flow. They are projected as `LocalMetadata`; entered email is shown as user-provided email, never as verified identity. The credential port no longer launches a default browser.
- Optional post-save browser opening is now performed by `BrowserProfileUiPort.OpenAsync(accountId, BrowserOpenTarget.ChatGpt)`, preserving managed per-account Chrome profiles, safe path validation, and truthful launch failure messages.
- Account timestamps in the information tab are rendered in local time without changing stored UTC values.
- Final AUTH-REAL-01 regression: Debug build `0` errors / `0` warnings, Release build `0` errors with existing analyzer warnings, architecture `8/8`, integration `109/109`. The current local endpoint was not running during the final smoke attempt; no live browser or provider claim is made.
- AUTH-REAL-01 report: `docs/ai/phase-reports/AUTH-REAL-01_REAL_ACCOUNT_CONNECTION_CERTIFICATION.md`. Live OAuth, browser session retention, provider identity, subscription, and usage remain unverified or blocked.

## CLI-01 Windows command launcher addendum (2026-10-16)

- Added the real `src/FlashAI.Cli` `flashrouter` apphost and `flashrouter.cmd` shim around the existing ASP.NET Core host. Commands: start/open/status/stop/restart/logs/version/help.
- Added authenticated current-user named-pipe control, data-root-scoped single-instance coordination, `/health/ready`, browser auto-open, and release-safe lifecycle handling. Control and runtime files are short-lived under `.local/runtime`; SQLite/Vault/key-ring paths remain unchanged.
- Added `tools/Install-FlashRouter.ps1` with staged copy, marked-install collision checks, idempotent PATH-file support, rollback backup, uninstall, and explicit `-ApplyUserPath` gating. No real User/Machine PATH or per-user installation was performed.
- Current validated package: `.local/releases/CLI-01/win-x64/package-release-v4`; self-contained `win-x64`, apphost enabled, 392 SHA-256 manifest entries, no local data or runtime secrets packaged.
- CLI-01 evidence: architecture `8/8`, integration `108/108`, installer sandbox GO, disposable release E2E command matrix GO except real browser-window auto-open intentionally not run. Overall CLI-01 is `PARTIAL`; real user install is `NOT AUTHORIZED`.
- See `docs/ai/phase-reports/CLI-01_WINDOWS_COMMAND_LAUNCHER.md` and `docs/runbooks/windows-cli.md` for exact commands, evidence, and remaining browser acceptance gap.

## UI-E / BE-03L / BE-04L Completion Addendum (2026-10-16)

- UI-E account connection and Chrome session behavior is `GO OFFLINE`: one-line credential parsing, encrypted Vault save, secure copy gates, account-scoped Chrome profile bindings, traversal rejection, launch failure truthfulness, and responsive runtime smoke are implemented.
- `App.razor` now resolves each module's current fingerprinted scoped-CSS asset from the static web asset provider. A fresh runtime returned HTTP 200 for `/` and all 11 module stylesheet links; the stale Accounts fingerprint is no longer emitted.
- BE-03L offline callback/security contracts are `GO`; live official OAuth remains `BLOCKED` until eligibility, registration, consent, and explicit live-test approval are available.
- BE-04L offline provider capability and usage handoff behavior is `GO`; live provider usage, quota, billing, and account verification are `NOT EXECUTED`.
- Final validation: build `0` errors; architecture `8/8`; integration `95/95`; focused UI-E `11/11`; targeted BE-03 OAuth `9/9`; disposable migration-copy verification `1/1`; read-only SQLite integrity verification `1/1`.
- Runtime preflight found the existing local database one migration behind and applied `AddBrowserProfileBindings`; the next startup reported up to date. No real account, credential, Vault value, backup, key-ring file, browser cookie, or provider session was used.
- Operational details are in `docs/runbooks/browser-profile.md` and `docs/runbooks/oauth.md`; phase evidence is in `docs/ai/phase-reports/UI-E_ACCOUNT_CONNECTION_CHROME_SESSION.md`, `BE-03L_OAUTH_CALLBACK.md`, and `BE-04L_USAGE_VERIFICATION.md`.

**Dự án:** FLASH ROUTER — Personal AI Account Manager  
**Giai đoạn hoàn thành:** UI Phase B (Feature-First & Vertical Slice Architecture Refactoring)  
**Tác giả bàn giao:** Antigravity UI & Solution Architecture Team  
**Đối tượng tiếp nhận:** Codex Backend Engineering Team  
**Mục tiêu:** Định nghĩa hợp đồng tích hợp typed DI ports, ViewModels, ánh xạ UI Actions đến Backend Use Cases, ma trận sở hữu tính năng Feature-First và lộ trình thực thi tầng Backend.

## UI-D / BE-04R Addendum (2026-10-16)

- Added `IAccountConnectionsReader` with separate owner-scoped list semantics; the existing latest-only `IOAuthConnectionStore.GetForAccountAsync` behavior remains unchanged for compatibility.
- SQLite and in-memory projections now expose connection metadata only: provider, connection kind, OAuth registration, credential kind, status, verified identity, scope, timestamps, provenance, granted scopes, and Vault-reference availability. Secret values and Vault payloads are never returned to UI.
- Added `IConnectionObservationsReader` and mapped normalized provider observations into the existing `SubscriptionTab`. Confirmed, stale, unsupported, permission, rate-limited, error, and unknown states remain distinct; missing values are not rendered as zero.
- Provider registry now lists six research-only metadata providers (`GitHub Copilot`, `Cursor`, `Windsurf`, `Kiro`, `Antigravity`, `Codex CLI`) without registering adapters or connectability. `Resolve` still resolves only implemented adapters.
- `ConnectionTab` is now a metadata-only multi-connection list with loading, error, empty, OAuth-blocked, historical-disconnected, credential-stored, and verified-identity states. Connection-specific mutation buttons are not shown where the existing backend port cannot safely target a selected connection.
- Browser certification used synthetic preview data only. Exact viewport checks passed at `375x812`, `768x1024`, `1024x768`, `1280x800`, and `1440x900`; no document or modal horizontal overflow was observed. Live OAuth and real provider quota checks remain blocked/not verified.
- Regression evidence after this addendum: build succeeds; architecture `8/8`; integration `84/84`; added BE-04R tests cover SQLite owner isolation, multiple registrations, stable ordering, stale/unsupported observations, no secret leakage, and research-only provider non-resolution.

## BE-06 Completion Addendum (2026-10-16)

- `BackupRestore` is integrated through `AddBackupRestoreModule(builder.Configuration)` and owns encrypted local backup manifests plus disposable restore validation at `/backups`.
- SQLite snapshots use the Online Backup API, require `integrity_check=ok`, are protected with the existing Windows-user Data Protection key ring, and carry a SHA-256 payload checksum plus safe schema/count metadata.
- Only encrypted vault ciphertext is copied; the DPAPI key ring, plaintext credentials, live providers, and active database replacement are excluded. Restore requires explicit confirmation and deletes its temporary database afterward.
- Regression evidence: architecture `8/8`, integration `73/73`; corrupted payload, name traversal, vault ciphertext inclusion, active-database preservation, and disposable restore tests pass.
- Release evidence: self-contained `win-x64` publish certified from `.local/releases/BE-06/win-x64-verified-3`; fresh isolated startup applied migrations, served `/`, `/connections`, `/backups`, and `/app.css` on `127.0.0.1`, then restarted and shut down cleanly. Browser smoke used Computer Use on the local host; viewport captures covered `375x812`, `768x900`, `1024x900`, `1280x900`, and `1440x900`.
- Security caveat: NuGet vulnerability scan reports transitive advisories for `System.Security.Cryptography.Pkcs 8.0.1` and `System.Security.Cryptography.Xml 8.0.4`; remediate or explicitly accept before a security-clean release claim. Live OAuth/provider verification remains `NOT VERIFIED`.

## BE-07 Addendum (2026-10-16)

- Baseline evidence: Release build `0 warnings, 0 errors`; architecture `8/8`; integration `75/75`; SQLite integrity `ok`, application-scoped foreign keys `1`, WAL, 6 migrations, and zero active accounts/connections/observations/check runs.
- Provider readiness now distinguishes six API adapters from six AI Web providers. All live plans are disabled and require explicit provider/connection/capability/request-limit approval. No live credentials, OAuth sessions, billable requests, or browser cookies were used.
- BE-07 fixed latest-observation retention after a failed provider check in both in-memory and SQLite stores and added synthetic regression tests. SQLite observation run ordering is performed after materializing run metadata because direct `DateTimeOffset` ordering is unsupported by the SQLite provider.
- A current-source self-contained artifact is certified at `.local/releases/BE-07/win-x64-verified-3` with 387 verified checksums. Isolated cold start served `/`, `/connections`, `/backups`, and `/app.css` on `127.0.0.1:5288` and applied migrations without packaging runtime data.
- .NET 10 SDK is not installed. The source remains `net8.0`; BE-07D is `UPGRADE READY` only and migration is not authorized or verified. BE-07E remains `NO-GO` until pagination, freshness, first-class time buckets, reconciliation, and the remaining accuracy tests are implemented.

---

## BE-01 Completion Addendum (2026-10-15)

BE-00 đã đạt `GO`. BE-01 đã triển khai Accounts persistence trên SQLite thật và giữ nguyên typed ports của UI:

- `IAccountWorkspaceReader` được composition sang `SqliteAccountWorkspaceReader` trong Integrated mode; `IAccountCommandPort` được composition sang `SqliteAccountCommandPort`.
- Preview vẫn tồn tại và chỉ được chọn khi `FlashAI:AccountStorageMode=Preview`; Integrated mode không seed demo và không fallback sang Preview khi SQLite lỗi.
- Database duy nhất là `.local/data/flashrouter.db`; migration assembly duy nhất thuộc host `FLASH ROUTER`.
- Đã áp dụng ba migration: `InitialAccountPersistence`, `AddProviderConnectionAccountForeignKey`, `AddTrackingDateTicksUtc`.
- Runtime evidence: migration lần đầu áp dụng thành công, lần restart sau báo database up to date; SQLite integrity `ok`, WAL bật, foreign keys bật, database sau cleanup có `0` account.
- Integration tests: `29/29` pass; architecture tests: `8/8` pass. Browser scenario create → reload → restart → update: pass. Responsive/visual matrix pass tại `375x812`, `768x900`, `1024x900`, `1280x900` và `1440x900`; không có document-level horizontal overflow.
- Browser certification cũng xác nhận archive/restore end-to-end: archived list/context menu và detail panel đều expose `Khôi phục tài khoản`/`Khôi phục`, gọi `RestoreAccountAsync` và cập nhật lại danh sách.

Các module Connections, Subscriptions, Authenticator, Reminders, Activity, ImportExport, BackupRestore, Settings và Diagnostics vẫn giữ implementation Preview theo phạm vi BE-01; BE-01 chỉ chuẩn bị metadata-only `ProviderConnections`, không lưu credential/token/password/cookie/TOTP seed và không thực hiện OAuth/API live.

## BE-02 Completion Addendum (2026-10-15)

- `ISecretVault` được composition sang `FileSecretVault` trong Integrated mode. Secret files nằm tại `.local/security/vault`; Data Protection key ring hiện hữu tại `.local/security` được giữ nguyên.
- Vault dùng authenticated encryption, hashed logical key names, key-bound purpose chain, atomic same-directory writes và envelope revision. Không lưu plaintext secret filename, database column hoặc log value.
- Authenticator Integrated mode dùng `DpapiAuthenticatorUiPort`: passphrase verifier PBKDF2, lock/unlock, TOTP credential ownership theo `AccountId`, masked metadata và RFC 6238 local generation. Preview mode vẫn là implementation synthetic riêng.
- `SecureCredentialImportParser` hỗ trợ legacy `email|password|totp` trong memory-only session; preview mask, duplicate/separator/validation errors và explicit encrypted commit đã được kiểm thử.
- Historical BE-02 checkpoint tests: architecture `8/8`, integration `41/41`, security focus `12/12`. Final certification expanded the security focus to `21/21` and the full integration suite to `59/59`.

## BE-03 Completion Addendum (2026-10-16)

- OpenAI Docs for Sign in with ChatGPT OSS plan usage, registration/sign-in, accounts/sessions, and errors/recovery were checked before implementation.
- Connections now contains offline-first OAuth registration/start, callback security, refresh, disconnect, ID-token/JWKS validation, loopback URI validation, and multi-account isolation contracts.
- `dynamic_agent_client` is used only as the first-registration contract value; issued client IDs are retained per registration and never hard-coded from another application.
- State, nonce, PKCE verifier, exact redirect URI, and attempt expiry are isolated per pending attempt. Replay, mismatch, expiry, wrong client, invalid subject, missing scopes, and invalid ID token paths are rejected.
- Access, refresh, and retained ID tokens are stored only through the BE-02 vault. SQLite stores OAuth registration/session metadata, vault references, and checks; no token values are stored in database columns.
- The additive migration `AddOAuthConnectionPersistence` was applied to the existing SQLite database after a workspace backup and integrity preflight.
- Offline OAuth tests are present and pass. Live eligibility, registration, browser authorization, token exchange, refresh, revocation, and plan usage remain `BLOCKED` / `LIVE NOT VERIFIED`.

## BE-04 Provider Foundation Addendum (2026-10-16)

- Connections now contains a capability-based provider integration foundation for OpenRouter, OpenAI Platform, Anthropic Platform, Gemini API, DeepSeek, and Groq. Adapters use fixed official HTTPS hosts and expose only provider-supported read-only capabilities.
- Provider credentials are resolved from the BE-02 vault using provider, connection, credential ID, and credential kind scope. SQLite stores credential references, capability metadata, check runs, normalized observations, and sync state; it never stores plaintext keys, authorization headers, or raw provider responses.
- Provider HTTP execution enforces official host validation, bounded response bodies, disabled redirects, cancellation/timeouts, safe error mapping, rate-limit classification, and retry classification. Administrative, project, standard, and API-key permissions remain distinct.
- Additive migrations `AddProviderIntegrationPersistence` and `AddProviderIntegrationForeignKeys` were applied after a database backup and disposable-copy preflight. SQLite integrity remained `ok`.
- BE-04 verdict: `OFFLINE GO`. Focused provider tests passed after security hardening; no real provider keys or live/billable requests were used. Live provider verification remains `NOT VERIFIED`.

## BE-05 Verification and Sync Addendum (2026-10-16)

- Manual checks require an authorized stored connection, resolve only supported capabilities, persist a check run and normalized observations transactionally, and keep unsupported, unauthorized, rate-limited, timeout, network, unavailable, cancelled, and invalid-response states distinct.
- `Check All` requires explicit confirmation after showing the eligible-connection and capability/billing-risk preview. It processes connections sequentially, honors cancellation, and returns completed partial results.
- Provider observation history preserves scope and time bucket identity; organization, project, workspace, key, model, and billing-account values are not silently summed. Previous observations remain when a later network check fails.
- `ProviderSyncScheduler` is registered but disabled by default. Scheduled checks exclude billable usage/cost capabilities by default and never perform password/MFA login or browser scraping.
- The existing `/connections` page renders the provider capability panel through isolated CSS without displaying credentials or simulating live success. BE-05 verdict: `OFFLINE GO`; combined focused tests passed `11/11`, with full regression at architecture `8/8` and integration `70/70`.

---

## 1. UI Contracts Inventory (Danh mục Hợp đồng Typed Ports)

Toàn bộ giao diện UI Phase B giao tiếp với tầng ứng dụng qua các **C# Typed Ports (DI Interfaces)** được đặt trong từng Module và `FlashAI.Foundation`. Không phụ thuộc trực tiếp vào DbContext, SQLite hay private HTTP endpoints.

| C# Interface | Assembly | Phạm vi & Trách nhiệm | Phương thức chính | ViewModels liên quan |
|---|---|---|---|---|
| `IAccountWorkspaceReader` | `FlashAI.Modules.Accounts` | Truy vấn danh sách và chi tiết tài khoản theo tìm kiếm, bộ lọc, sắp xếp | `GetAccountsAsync(search, filter, sort, ct)`<br/>`GetAccountDetailAsync(accountId, ct)` | `AccountListItemVm`<br/>`AccountDetailVm` |
| `IAccountCommandPort` | `FlashAI.Modules.Accounts` | Thao tác CRUD, đánh dấu yêu thích, lưu trữ, xóa tài khoản | `CreateAccountAsync(model, ct)`<br/>`UpdateAccountAsync(id, model, ct)`<br/>`ToggleFavoriteAsync(id, ct)`<br/>`ArchiveAccountAsync(id, ct)`<br/>`RestoreAccountAsync(id, ct)`<br/>`DeleteAccountAsync(id, ct)` | `AccountEditorVm`<br/>`OperationResultVm` |
| `IConnectionUiPort` | `FlashAI.Modules.Connections` | Truy vấn trạng thái ủy quyền OAuth và điều phối kết nối | `GetConnectionStatusAsync(accountId, ct)`<br/>`InitiateConnectionAsync(accountId, ct)`<br/>`DisconnectAsync(accountId, ct)` | `ConnectionStatusVm`<br/>`OperationResultVm` |
| `ISubscriptionUiPort` | `FlashAI.Modules.Subscriptions` | Quản lý thông tin gói sử dụng và lịch sử quan sát | `GetObservationAsync(accountId, ct)`<br/>`RecordObservationAsync(accountId, plan, source, notes, ct)` | `SubscriptionObservationVm`<br/>`OperationResultVm` |
| `IAuthenticatorUiPort` | `FlashAI.Modules.Authenticator` | Bảo mật két 2FA, mở khóa, truy vấn mã TOTP | `IsVaultLockedAsync(ct)`<br/>`UnlockVaultAsync(passphrase, ct)`<br/>`LockVaultAsync(ct)`<br/>`GetCredentialsAsync(accountId, ct)`<br/>`GetCurrentCodeAsync(credentialId, ct)`<br/>`AddCredentialAsync(accountId, label, secret, ct)`<br/>`RemoveCredentialAsync(credentialId, ct)` | `TotpCredentialVm`<br/>`TotpCodeVm`<br/>`OperationResultVm` |
| `IImportExportUiPort` | `FlashAI.Modules.ImportExport` | Xem trước file CSV và xuất dữ liệu an toàn | `PreviewCsvAsync(fileName, stream, ct)`<br/>`CommitImportAsync(previewId, conflictStrategy, ct)`<br/>`ExportAccountsAsync(options, ct)` | `ImportPreviewResultVm`<br/>`ExportOptionsVm`<br/>`OperationResultVm` |
| `IReminderUiPort` | `FlashAI.Modules.Reminders` | Quản lý danh sách nhắc nhở gia hạn & bảo mật | `GetRemindersAsync(accountId, ct)`<br/>`AddReminderAsync(accountId, title, type, dueDate, ct)`<br/>`ToggleCompletedAsync(reminderId, ct)`<br/>`DeleteReminderAsync(reminderId, ct)` | `ReminderVm`<br/>`OperationResultVm` |
| `IActivityUiPort` | `FlashAI.Modules.Activity` | Nhật ký sự kiện bảo mật và hoạt động tài khoản | `GetEventsAsync(accountId, level, ct)` | `ActivityEventVm` |
| `ISettingsUiPort` | `FlashAI.Modules.Settings` | Lưu trữ và đọc cấu hình người dùng | `GetSettingsAsync(ct)`<br/>`SaveSettingsAsync(settings, ct)` | `UserSettingsVm`<br/>`OperationResultVm` |
| `IDiagnosticsUiPort` | `FlashAI.Modules.Diagnostics` | Thu thập thông tin chẩn đoán hệ thống an toàn | `GetDiagnosticsAsync(ct)` | `DiagnosticsVm` |

---

## 2. UI Action to Backend Use Case Mapping (Ánh xạ Thao tác UI)

| UI Component | Event Handler UI | C# Port được gọi | Use Case Backend tương ứng (Codex cần hiện thực) | Trạng thái UI khi thành công | Trạng thái UI khi lỗi / chưa có BE |
|---|---|---|---|---|---|
| `HeaderBar` | `OnAddClicked` | `CreateAccountDialog` | Mở dialog thêm tài khoản | Hiển thị modal tạo mới | N/A |
| `CreateAccountDialog` | `OnSubmit` | `IAccountCommandPort.CreateAccountAsync` | `CreateAccountUseCase` (Validate định dạng email, kiểm tra trùng lặp SQLite, sinh UUID) | Đóng modal, chọn tài khoản mới, refresh danh sách, toast thành công | Hiển thị lỗi inline cạnh input tương ứng |
| `AccountListItem` | `Click` | `IAccountWorkspaceReader.GetAccountDetailAsync` | `GetAccountDetailQuery` | Highlight row, hiển thị detail panel bên phải, nạp thông tin các tab | Giữ nguyên selection cũ hoặc hiển thị Empty State |
| `AccountListItem` | `Star Click` | `IAccountCommandPort.ToggleFavoriteAsync` | `ToggleFavoriteAccountCommand` | Đổi icon sao vàng, refresh danh sách | Toast thông báo lỗi |
| `AccountDetailPanel` | `Click Lưu thay đổi` | `IAccountCommandPort.UpdateAccountAsync` | `UpdateAccountUseCase` (Ghi nhận thay đổi displayName, tags, notes vào SQLite) | Thoát edit mode, reset dirty state, toast thành công | Giữ dirty mode, hiển thị lỗi validation |
| `AccountDetailPanel` | `Click Xóa` | `IAccountCommandPort.DeleteAccountAsync` | `DeleteAccountUseCase` (Xóa mềm hoặc xóa cứng kèm cascade audit) | Đóng modal xóa, chọn tài khoản kế tiếp, refresh danh sách | Toast thông báo lỗi |
| `FilterBar` | `Click Pill` | `IAccountWorkspaceReader.GetAccountsAsync` | `FilterAccountsQuery` (Lọc theo trạng thái, tag, yêu thích) | Đổi active pill, nạp tập tài khoản tương ứng, tự chọn tài khoản đầu tiên | N/A |
| `ConnectionTab` | `Click Kết nối` | `IConnectionUiPort.InitiateConnectionAsync` | `InitiateOAuthFlowUseCase` (Mở loopback HTTP listener, chuyển hướng browser đến OpenAI OAuth) | Cập nhật badge `Đã kết nối`, hiển thị thông tin identity | Hiện thị trạng thái truthful: `Chờ tích hợp backend chính thức (Phase 1)` |
| `AuthenticatorTab` | `Countdown 30s` | `IAuthenticatorUiPort.GetCurrentCodeAsync` | `GenerateTotpCodeQuery` (Đọc DPAPI vault, giải mã RFC 6238 TOTP tại local) | Cập nhật 6 số mã xác thực và vòng xoay thời gian | Hiển thị trạng thái Vault Locked nếu chưa mở khóa |
| `ImportExportDialog`| `Upload CSV` | `IImportExportUiPort.PreviewCsvAsync` | `ParseCsvPreviewQuery` (Đọc cấu trúc file, kiểm tra cột, phát hiện trùng lặp) | Hiển thị bảng xem trước (preview table), danh sách bản ghi hợp lệ / lỗi | Báo lỗi định dạng file CSV |
| `ImportExportDialog`| `Xác nhận nhập`| `IImportExportUiPort.CommitImportAsync` | `CommitImportAccountsCommand` (Transaction ghi hàng loạt vào SQLite) | Hiển thị kết quả nhập, làm mới danh sách tài khoản | Hiển thị lỗi xung đột bản ghi |
| `SettingsDialog` | `Lưu cấu hình` | `ISettingsUiPort.SaveSettingsAsync` | `SaveUserSettingsCommand` (Lưu theme, privacy masking vào JSON/SQLite) | Áp dụng cấu hình lên circuit, toast thành công | Báo lỗi lưu |

---

## 3. Feature Ownership Matrix (Ma trận Sở hữu Tính năng Cụ thể theo Vertical Slice)

Codex sẽ phát triển tầng Backend trực tiếp tại các thư mục Feature tương ứng đã được thiết lập sẵn khung kiến trúc:

| Module | Feature Slice | Vị trí UI & CSS | Vị trí Backend Codex triển khai | Domain / Persistence dùng chung | Trạng thái Hiện tại |
|---|---|---|---|---|---|
| **Accounts** | `BrowseAccounts` | `Features/BrowseAccounts/UI/AccountList.razor`<br/>`AccountListItem.razor` | `Features/BrowseAccounts/Application/SqliteAccountWorkspaceReader.cs`<br/>(Query Accounts, Filter, Sort) | `Domain/AccountRecord.cs`<br/>`Persistence/AccountModelContributor.cs` | UI Ready / BE-01 Integrated |
| **Accounts** | `AccountDetails` | `Features/AccountDetails/UI/AccountDetailPanel.razor`<br/>`AccountInformationTab.razor` | `SqliteAccountWorkspaceReader.GetAccountDetailAsync` | `Domain/AccountRecord.cs`<br/>`Persistence/AccountProjectionMapper.cs` | UI Ready / BE-01 Integrated |
| **Accounts** | `CreateAccount` | `Features/CreateAccount/UI/CreateAccountDialog.razor` | `SqliteAccountCommandPort.CreateAccountAsync`<br/>(validation, duplicate rule, transaction) | `Domain/AccountRecord.cs`<br/>`Persistence/AccountModelContributor.cs` | UI Ready / BE-01 Integrated |
| **Accounts** | `EditAccount` | `Features/EditAccount/UI/UnsavedChangesDialog.razor` | `SqliteAccountCommandPort.UpdateAccountAsync`<br/>(validation, compare-and-swap) | `Domain/AccountRecord.cs`<br/>`Persistence/AccountModelContributor.cs` | UI Ready / BE-01 Integrated |
| **Accounts** | `ArchiveAccount` | `Features/ArchiveAccount/UI/ArchiveAccountDialog.razor` | `SqliteAccountCommandPort.ArchiveAccountAsync` / `RestoreAccountAsync` | `Domain/AccountRecord.cs`<br/>`Persistence/AccountModelContributor.cs` | UI Ready / BE-01 Integrated |
| **Accounts** | `DeleteAccount` | `Features/DeleteAccount/UI/DeleteAccountDialog.razor` | `SqliteAccountCommandPort.DeleteAccountAsync`<br/>(explicit UI confirmation, cascade owned tags) | `Domain/AccountRecord.cs`<br/>`Persistence/AccountModelContributor.cs` | UI Ready / BE-01 Integrated |
| **Connections** | `ManageConnections` | `Features/ManageConnections/UI/ConnectionTab.razor` | `Features/ManageConnections/Application/`<br/>(PKCE Loopback OAuth Client + provider verification/sync) | `Domain/ConnectionRecord.cs`<br/>`Domain/ProviderObservationRecords.cs`<br/>`Persistence/ProviderObservationStores.cs` | UI Ready / BE-03/04/05 Offline Integrated |
| **Subscriptions** | `ManageSubscriptions` | `Features/ManageSubscriptions/UI/SubscriptionTab.razor` | `Features/ManageSubscriptions/Application/`<br/>(Record / Sync Subscription) | `Domain/SubscriptionRecord.cs`<br/>`Persistence/SubscriptionDbContext.cs` | UI Ready / Preview Active |
| **Authenticator** | `TotpVault` | `Features/TotpVault/UI/AuthenticatorTab.razor` | `Application/DpapiAuthenticatorUiPort.cs`<br/>`Application/Rfc6238TotpGenerator.cs` | `Domain/TotpCredential.cs`<br/>`.local/security/vault/ (DPAPI/Data Protection)` | UI Ready / BE-02 Integrated |
| **Reminders** | `ManageReminders` | `Features/ManageReminders/UI/ReminderTab.razor` | `Features/ManageReminders/Application/`<br/>(Reminder CRUD & Background Worker) | `Domain/ReminderRecord.cs`<br/>`Persistence/ReminderDbContext.cs` | UI Ready / Preview Active |
| **Activity** | `ActivityLog` | `Features/ActivityLog/UI/ActivityTab.razor` | `Features/ActivityLog/Application/`<br/>(Append-only Security Event Logger) | `Domain/ActivityEvent.cs`<br/>`Persistence/ActivityDbContext.cs` | UI Ready / Preview Active |
| **ImportExport** | `ImportExportWizard` | `Features/ImportExportWizard/UI/ImportExportDialog.razor` | `Features/ImportExportWizard/Application/`<br/>(CSV Streaming Parser & Exporter) | `Domain/ImportExportModels.cs` | UI Ready / Preview Active |
| **BackupRestore** | `BackupManager` | `Features/BackupManager/UI/BackupRestoreDialog.razor` | `Features/BackupManager/Application/`<br/>(Encrypted Archive & Snapshot Service) | `Domain/BackupMetadata.cs` | UI Ready / Preview Active |
| **Settings** | `ManageSettings` | `Features/ManageSettings/UI/SettingsDialog.razor` | `Features/ManageSettings/Application/`<br/>(Local Config Persistence) | `appsettings.local.json` | UI Ready / Circuit Storage |
| **Diagnostics** | `SystemHealth` | `Features/SystemHealth/UI/DiagnosticsPanel.razor` | `Features/SystemHealth/Application/`<br/>(System Telemetry & Health Checks) | `Foundation/DiagnosticModels` | UI Ready / Live Metrics |
| **Dashboard** | `WorkspaceOverview` | N/A (Orchestration only) | `Features/WorkspaceOverview/Application/`<br/>(Aggregated Read Models) | N/A | Structure Ready |

---

## 4. Backend TODO Matrix (Kế hoạch Triển khai Tầng Backend của Codex)

| Hạng mục | Mức ưu tiên | Nhiệm vụ kỹ thuật | Hướng dẫn triển khai |
|---|---|---|---|
| **Accounts Persistence** | Hoàn thành BE-01 | Composition `SqliteAccountWorkspaceReader` + `SqliteAccountCommandPort` trong Integrated mode | EF Core SQLite, `AccountProfiles`, `AccountTags`, validation, filtering, lifecycle, optimistic concurrency, migrations and integration tests. |
| **Official Authorization** | P0 (Bắt buộc) | Triển khai `IOAuthClient` chính thức cho OpenAI | Sử dụng Authorization Code Flow with PKCE, loopback listener trên `127.0.0.1:port`. Tuyệt đối không đọc cookie/session browser hoặc reverse engineering. |
| **DPAPI Vault Storage** | Hoàn thành BE-02 | Composition `DpapiAuthenticatorUiPort` + `FileSecretVault` trong Integrated mode | Data Protection với Windows DPAPI user scope, encrypted TOTP envelopes, PBKDF2 unlock verifier, masked index, RFC 6238 và security tests. |
| **Subscriptions Persistence**| P1 | Triển khai bảng `SubscriptionHistory` | Lưu trữ quan sát định kỳ gói cước, phân biệt rõ ràng giữa gói người dùng tự khai và gói xác nhận từ provider. |
| **CSV Import / Export Engine**| P1 | Triển khai `CsvAccountParser` và `CsvAccountExporter` | Đọc stream CSV, xử lý duplicate detection theo email, xuất file chỉ chứa metadata an toàn (không xuất secrets/passwords). |
| **Background Reminders** | P2 | Triển khai `SqliteReminderRepository` | Quản lý nhắc nhở, kiểm tra hạn gia hạn cước. |
| **Local Security Audit Log** | P2 | Triển khai `SqliteActivityLogger` | Ghi nhật ký sự kiện, kiểm tra kết nối, mở khóa vault (lưu ý: không ghi secrets hoặc raw tokens vào log). |
| **Settings Storage** | P2 | Triển khai lưu trữ `appsettings.local.json` | Lưu trữ cấu hình giao diện, che mờ email, chế độ riêng tư. |

---

## 5. Integration Rules (Quy tắc Tích hợp Bắt buộc)

1. **Tuân thủ Boundary Architecture (Rule 31):**
   - Các module độc lập (`Accounts`, `Connections`, `Subscriptions`, v.v.) chỉ được reference vào `FlashAI.Foundation`. Không được tạo reference chéo giữa các module.
   - Host `FLASH ROUTER` chịu trách nhiệm DI composition.
2. **Bảo tồn Giao diện Đã Duyệt:**
   - Codex không được thay đổi cấu trúc Razor Component, layout 2 cột, CSS Isolation, hoặc thêm Sidebar/Dashboard admin.
   - Mọi kết quả nghiệp vụ phải phản hồi qua `OperationResultVm` hoặc Typed ViewModels đã định nghĩa.
3. **Một SQLite Database Duy Nhất:**
   - Không chia nhỏ database theo từng feature. Toàn bộ persistence của ứng dụng tập trung vào một CSDL SQLite duy nhất được điều phối tập trung.
4. **Data Safety & Security Boundary:**
   - Ứng dụng chỉ lắng nghe trên `127.0.0.1` / `localhost`.
   - Không được hardcode credentials hoặc gửi dữ liệu ra bên ngoài.
   - Khi backend chưa sẵn sàng cho một tính năng (ví dụ OAuth), giữ nguyên trạng thái hiển thị trung thực `Chờ tích hợp backend`, không được trả kết quả "Thành công" giả lập trong môi trường Production.
5. **Testing Integrity:**
   - Mọi service mới do Codex viết phải có Unit/Integration test tương ứng.
   - Đảm bảo architecture tests và SQLite integration tests tiếp tục PASS; hiện tại `8/8` architecture và `75/75` integration tests pass.

---

## 6. UI-C Account Connection Status (2026-10-16)

- UI-C đã hoàn tất trong phạm vi offline/local; báo cáo nghiệm thu nằm tại `docs/ai/phase-reports/UI-C_ACCOUNT_CONNECTION_UX_CORRECTION.md`.
- Primary account UI hiện chỉ hiển thị bốn filter: Tất cả, Đã kết nối, Chưa xác minh, Cần chú ý; Settings không còn ở header; Favorite/Tags fields và API vẫn được giữ cho tương thích dữ liệu nhưng control đã được loại khỏi UI chính.
- Add Account hiện có hai phương thức: OAuth ChatGPT chính thức và credential + TOTP seed tùy chọn. OAuth live vẫn bị chặn nếu chưa xác nhận eligibility.
- Verification cuối: build `0` lỗi; UI-C `5/5`; integration `80/80`; architecture `8/8`; tổng `88` test pass.
- Runtime smoke checks offline đạt; responsive matrix 375/768/1024/1280/1440 chưa hoàn tất nên overall UI-C được ghi nhận là `PARTIAL`.

## AUTH-BROWSER-01 Controlled ChatGPT Browser Authentication (2026-10-16)

- Added `IBrowserAuthOrchestrator` with explicit email, password, MFA, human-takeover, external-provider, observed-session, profile-lock, timeout, cancellation, and unknown-state results. Attempts are in-memory only and expire after ten minutes.
- Added a Playwright for .NET headed persistent-context adapter. It launches only the managed account profile, uses the installed Chrome executable, rejects profile locks without terminating processes, validates official HTTPS origins before navigation and each sensitive fill, and never reads cookies, storage, tokens, or raw page HTML.
- Added stable attribute-based page detection for email, password, and one-time-code fields. CAPTCHA, SSO, passkey, recovery-code, SMS/email-code, and unknown states stop for human takeover.
- Added `BrowserAuthCredentialReader`, which resolves the selected account's manual-web credential reference by `AccountId`, requires Vault access before password/TOTP reads, and never puts secrets in the attempt or result model. TOTP uses the Foundation local contract implemented by the existing BE-02 RFC 6238 generator.
- Added a one-page `Hỗ trợ đăng nhập` dialog with separate confirmation actions for email, password, and TOTP. `BrowserSessionObserved` remains distinct from OAuth authorization and verified identity.
- Synthetic AUTH-BROWSER-01 tests pass `5/5`; no real account, live ChatGPT login, browser cookie, session token, or supplied credential was used.
- The workspace directory is not currently recognized as a Git checkout; no repository initialization, commit, push, or deployment was performed.

## AUTH-BROWSER-01 P0 DI Startup Hotfix (2026-10-16)

- Root cause: `BrowserProfileUiPort` kept both the legacy four-dependency public constructor and the canonical `IBrowserProfileManager` + `IChromeProcessLauncher` constructor, while `ConnectionsModuleRegistration` used implicit `AddScoped<BrowserProfileUiPort>()`. Both constructor dependency sets were resolvable, so `builder.Build()` failed with constructor ambiguity at `Program.cs:45`.
- Minimal fix: `src/Modules/Connections/ConnectionsModuleRegistration.cs:76-81` now uses an explicit factory for the canonical constructor. The legacy public constructor remains for compatibility; BrowserProfileManager, Vault, SQLite, DPAPI keys, and Chrome profiles were not rebuilt or changed.
- Added `tests/FlashAI.IntegrationTests/FlashRouterHostCompositionTests.cs`, which builds the complete host composition with `ValidateOnBuild=true` and `ValidateScopes=true`, then resolves browser profile and browser-auth services. DI-focused result: `1/1`.
- Regression results: architecture `8/8`, full integration `115/115`, Debug build `0 errors`, Release build `0 errors` (24 existing analyzer warnings).
- Runtime results: `http` launch profile started on `http://localhost:5270`; health, `/`, Blazor script, app CSS, bundled CSS, and all 11 module CSS bundles returned `200`. `https` launch profile started on `https://localhost:7146` plus HTTP `5270`; HTTPS health and `/` returned `200`.
- Runtime verification used only disposable `.local/test-data/di-startup-runtime-*` roots. No supplied credentials, live provider/login/API access, cookies/tokens, real SQLite/Vault/DPAPI data, existing accounts, or Chrome profiles were touched.
- Detailed report: `docs/ai/phase-reports/AUTH-BROWSER-01_DI_STARTUP_HOTFIX.md`. P0 verdict: **GO**.

## AUTH-BROWSER-04 Real ChatGPT Login Routing Hotfix (2026-10-16)

- Root cause: the integrated browser session boundary allowed a caller-supplied destination while the local E2E composition exposed `MockAuthenticationServer.LoginUri` (`127.0.0.1:<port>/login`). The production path now fails closed unless the start URI is exactly `https://chatgpt.com/`; no production `BaseUrl`, `LoginUrl`, `AuthUrl`, `TestServer`, or `MockAuth` override selects the destination.
- `BrowserAuthDialog` -> `MainWorkspace.HandleAssistLogin` -> `BrowserAuthOrchestrator.StartAsync` -> `IBrowserProfileManager.PrepareAsync` -> production `PlaywrightBrowserAuthenticationSessionFactory` is now evidenced end-to-end. Redirects and every sensitive fill are origin-checked; credentials remain gated by confirmation and Vault state.
- `PlaywrightBrowserAuthenticationSession` recognizes ChatGPT's multiple guest login buttons as one explicit semantic CTA (`data-mobile-auth-entry-action="login"`) while retaining fail-closed ambiguity handling for generic candidates. It starts at the public landing page and does not hard-code `/login`.
- Regression/runtime results: Debug integration `138/138`, Release integration `138/138`, Debug architecture `8/8`, Release architecture `8/8`, Debug/Release solution builds `0` errors. Real host HTTP `5270` and HTTPS `7146` readiness/assets checks passed; headed production probe reached `https://chatgpt.com/` -> `GuestLandingLoginAvailable` -> `EmailRequired` using a disposable profile and no credentials.
- Post-run cleanup: no `5270`/`7146` listener, managed host runtime files removed, and no live probe profile directory remained. Existing Vault, SQLite, DPAPI keys, account data, and Chrome profile data were not migrated or deleted.
- Detailed report: `docs/ai/phase-reports/AUTH-BROWSER-04_REAL_LOGIN_ROUTING_HOTFIX.md`. Routing verdict: **GO**; credential submission and identity verification remain intentionally out of scope for this hotfix.

## AUTH-BROWSER-05/06 Real ChatGPT Acceptance Boundary (2026-10-17)

- The email-step hardening is complete: `PlaywrightBrowserAuthenticationSession` scopes English/Vietnamese email Continue actions to the email form, excludes provider alternatives, and fails closed on missing or ambiguous actions. `BrowserAuthOrchestrator` performs the approved email fill, activates the scoped Continue action, and polls for the next state.
- Current validation is clean after headed fixture serialization: Debug and Release architecture tests are `8/8`; Debug and Release integration tests are `141/141`; Debug and Release solution builds are `0` warnings and `0` errors.
- Real headed production evidence remains credential-free: disposable managed profile, `ChromiumSandbox = true`, `https://chatgpt.com/` -> guest login CTA -> official email form. No password, TOTP, cookie, token, provider API, or existing Chrome profile was used.
- AUTH-BROWSER-06 is **WAITING FOR USER** for authenticated acceptance because the user-pasted account values were not used and no password/TOTP rotation, Vault update, or explicit in-app live approval was established. Public routing is **GO**; credential submission, authenticated session, and persistence are not certified.
- The current workspace has no listener on `5270` or `7146`. Existing stale `.local/runtime` metadata was observed and intentionally left untouched; no SQLite, Vault, DPAPI, account, or Chrome data was deleted.
- Detailed reports: `docs/ai/phase-reports/AUTH-BROWSER-05_REAL_EMAIL_MODAL_CREDENTIAL_FLOW.md` and `docs/ai/phase-reports/AUTH-BROWSER-06_REAL_CHATGPT_LOGIN_ACCEPTANCE.md`.

## AUTH-BROWSER-07 Real Account Login Certification (2026-10-17)

- Offline certification is **GO**. The explicit `BrowserProfileUiPort` DI factory remains the minimal fix for the constructor ambiguity; the legacy public constructor remains for compatibility while Integrated mode resolves the canonical `IBrowserProfileManager` + `IChromeProcessLauncher` path.
- The full host composition regression uses `ValidateOnBuild = true` and `ValidateScopes = true`, builds successfully, and resolves `IBrowserProfileUiPort`, `IBrowserAuthOrchestrator`, `IBrowserProfileManager`, and `IChromeProcessLauncher` without ambiguity.
- Fresh validation: Debug and Release solution builds pass with 0 errors; architecture tests pass `8/8` in both configurations; integration tests pass `142/142` in both configurations with 0 skips. Builds report 35 existing analyzer warnings per configuration.
- Disposable runtime evidence: the `http` launch profile served `http://localhost:5270`; the `https` launch profile served `https://localhost:7146` plus HTTP `5270`; readiness, root HTML, Blazor assets, and the Connections module CSS bundle returned `200`.
- Public routing remains credential-free and official: `https://chatgpt.com/` -> official `Đăng nhập` CTA -> official email form in the prior headed production probe. No mock URL, password, TOTP, cookie, token, provider API, or existing Chrome profile was used.
- AUTH-BROWSER-07 LIVE is **WAITING FOR USER**. The previously pasted account values are treated as compromised; password rotation, TOTP reset/re-enrollment, Vault update, old-credential invalidation, and explicit in-app approval are required before any sensitive action.
- Detailed report: `docs/ai/phase-reports/AUTH-BROWSER-07_REAL_ACCOUNT_LOGIN_CERTIFICATION.md`.

## AUTH-BROWSER-08 One-Click Auto Login (2026-10-17)

- One-click Auto Login is implemented through the existing `BrowserAuthOrchestrator`; it keeps one headed Playwright context/profile lease and advances through existing-session detection, official login CTA, email, Vault-gated password, local TOTP, and authenticated UI observation.
- Added memory-only `AutoLoginAttemptConsent`, `BrowserAuthPreflight`, and `BrowserAuthProgress` contracts. Consent is account/provider/profile-bound, time-limited, revocable, single-attempt, and protected by a per-account concurrent-run gate.
- `BrowserAuthDialog` now presents one clear approval screen with masked account, Vault and credential-reference readiness, profile status, official destination, and live progress. The account action is labeled `Auto Login`.
- Vault readiness is checked without reading plaintext secrets during preflight. Password and TOTP values are read only from the selected account's encrypted Vault reference after the approved workflow reaches the corresponding official-origin state.
- Cancellation, expiry, unsupported challenges, browser closure, invalid origin, and profile failures revoke consent and release the owned session/profile lease without deleting persistent profile data.
- Validation: Debug/Release architecture `8/8`; Debug integration `149/149` in `3m21s`; Release integration `149/149` in `3m36s`; both with 0 skipped; prior focused Auto Login coverage `11/11`; final `FullyQualifiedName~AutoLogin` rerun `7/7`; headed synthetic Auto Login `1/1`; final Debug and Release solution builds `0` warnings / `0` errors. Test compilation still prints 7 pre-existing analyzer warnings in unrelated test code.
- The headed test progress collector was changed from a non-thread-safe `List<string>` to `ConcurrentQueue<string>` after a real callback/assertion race was observed; the final focused and full suites pass.
- Headed synthetic persistence timing repeated three times: `13s`, `13s`, `12s`. Stage-level timing instrumentation was not added; no stage timings or invented median are claimed.
- Runtime: disposable host `127.0.0.1:5298` returned health/root/Blazor/all 17 CSS checks `200`; Visual Studio `http://localhost:5270` and `https://localhost:7146` profiles returned health/root `200`; all listeners and disposable roots were cleaned up.
- Real ChatGPT credential acceptance and real session persistence remain `WAITING FOR USER / BLOCKED`. The previously pasted credential was not used. A live attempt requires password rotation, TOTP reset/re-enrollment, local Vault update, old-credential invalidation, and explicit in-app approval for one selected account.
- Detailed report: `docs/ai/phase-reports/AUTH-BROWSER-08_ONE_CLICK_AUTO_LOGIN.md`.

## AUTH-BROWSER-09 Real One-Click Live Certification (2026-10-17)

- Re-audited the complete account-selection -> preflight -> one-time consent -> `BrowserAuthOrchestrator.AutoLoginAsync` -> managed profile -> Playwright -> Vault/TOTP -> authenticated UI observation path. No new application source defect was found and no broad refactor was made.
- Production start remains official-only: `https://chatgpt.com/`. Playwright remains headed with `ChromiumSandbox = true`; mock login URLs remain test-only.
- A Development launch using the configured `http://localhost:5270` profile was blocked by an existing `FLASH_ROUTER.exe` process already owning port `5270`; it was not killed or modified. An isolated disposable Integrated host on `http://127.0.0.1:5299` started successfully.
- Disposable host evidence: health `200` in `469ms`, root `200` in `1,676ms`, Blazor script `200` in `52ms`, and 17/17 discovered CSS assets returned `200`. No listener remains on `5270`, `5299`, or `7146`.
- Chrome public probe opened `https://chatgpt.com/` in a new Chrome tab and observed an existing authenticated UI without entering credentials. This is public-route/session evidence only, not selected-account managed-profile acceptance.
- Real password/TOTP acceptance, authenticated selected-account observation, and close/reopen persistence remain `NOT EXECUTED / WAITING FOR USER` because the exposed credential was not used and no rotated Vault credential plus in-app approval was available.
- Performance is `PARTIAL`: host endpoint timings and total synthetic headed runs (`13s`, `13s`, `12s`) are recorded; stage-level browser telemetry is not instrumented, so no invented stage timings or median is claimed.
- Disposable AUTH-BROWSER-09 runtime directories remain under `.local/test-data/auth-browser-09-runtime-*` because cleanup was rejected by the environment safety policy. They contain no real user data and are distinct from the real data/security/profile roots.
- Detailed report: `docs/ai/phase-reports/AUTH-BROWSER-09_REAL_ONE_CLICK_LIVE_CERTIFICATION.md`.

## AUTH-BROWSER-10 Interactive Live Acceptance (2026-10-17)

- Added lightweight redacted stage telemetry. `BrowserAuthPreflight.Timing` records preflight duration; `BrowserAuthResult.Timing` records only stages executed by the attempt. Playwright reports profile-lease, headed Chrome launch, and official navigation durations through an optional diagnostics seam. No password, TOTP seed/code, cookie, token, local storage, raw HTML, or query token is included.
- Added fail-closed credential-rotation attestation to the user-facing BrowserAuthDialog. Auto Login requires the user to confirm in FLASH ROUTER that password and TOTP were replaced after prior exposure; missing attestation returns `CREDENTIAL_ROTATION_ATTESTATION_REQUIRED` before opening Chrome or reading Vault secrets. Consent remains attempt/account/provider/profile scoped and memory-only.
- Updated `BrowserAuthDialog`, `MainWorkspace`, and isolated CSS only for the attestation control; no SQLite, Vault, DPAPI/Data Protection key, browser-profile, migration, provider, Tier 5, or Tier 6 changes were made.
- Final regression evidence: Debug and Release solution builds pass with analyzers enabled at `0 warnings / 0 errors`; Debug and Release architecture tests `8/8`; Debug and Release integration tests `151/151` on the final runs; focused AUTH-BROWSER-01 tests `13/13`; headed synthetic Auto Login E2E `1/1` in Release. An earlier Debug full-suite attempt transiently reported `150/151` because a disposable Chromium profile still held `Account Web Data`; the immediate retry passed `151/151`.
- Disposable Integrated host `http://127.0.0.1:5299` returned health `200`, root `200`, `/_framework/blazor.web.js` `200`, and `17/17` CSS assets `200`, then stopped. No listener remains on `5270`, `5299`, or `7146`.
- Public official ChatGPT navigation remains verified without credential submission. Real credential submission, authenticated-session identity acceptance, and session reopen remain `WAITING FOR USER`; the previously exposed credential was not used.
- Report: `docs/ai/phase-reports/AUTH-BROWSER-10_INTERACTIVE_LIVE_ACCEPTANCE.md`.

## AUTH-BROWSER-11 P0 Runtime Blocker Remediation (2026-10-17)

- Fixed two fresh regression defects found during implementation: readiness now reports `VAULT_LOCKED` before reference-missing/provider-mismatch details, and existing credential replacement no longer asks SQLite to order a `DateTimeOffset` expression it cannot translate. Replacement materializes only the matching candidate set, orders locally, and preserves `ConnectionId` plus credential-reference identity.
- Embedded Vault unlock and encrypted credential replacement controls are present in `BrowserAuthDialog`; `MainWorkspace` reuses `IAuthenticatorUiPort`, refreshes preflight, preserves the selected `AccountId`, and clears passphrases/password/TOTP input after use. `ProgressMessage` is explicitly bound so `_browserAuthProgressMessage` is not rendered literally.
- Historical persisted `LaunchFailed` is now presented as retryable history (`BrowserProfileRetryAvailable`) rather than a current attempt failure. Fresh launch failures still go through the existing ownership/profile manager path. Chrome managed launch adds `--no-first-run`, `--no-default-browser-check`, and `--disable-sync` without disabling Chromium sandbox or changing existing profiles.
- Fresh verification: Debug build `0 warnings / 0 errors`; Release build `38 analyzer warnings / 0 errors`; Architecture `8/8` Debug and Release; Integration `154/154` Debug and Release; headed Playwright filter `13/13` Debug and Release; focused AUTH-BROWSER-01 + AUTH-BROWSER-11 `16/16`.
- Full DI validation uses `ValidateOnBuild=true` and `ValidateScopes=true`; `IBrowserProfileUiPort`, `IBrowserAuthOrchestrator`, `IBrowserProfileManager`, and `IChromeProcessLauncher` resolve from the complete Integrated composition.
- Fresh disposable Host evidence: `http://127.0.0.1:5292` returned HTTP 200 for readiness, root, Connections, Blazor JS, and all `17/17` discovered CSS assets. Visual Studio `http` profile `http://localhost:5270` and `https` profile `https://localhost:7146` both started and returned HTTP 200 probes. CUA inspected the rendered FLASH ROUTER workspace and Connections page.
- No live account, Vault passphrase, provider password/TOTP, cookie, token, localStorage, provider API, existing SQLite/Vault/DPAPI data, or existing Chrome profile was touched. Real Vault unlock, live credential submission, authenticated identity acceptance, and session reopen remain `WAITING FOR USER / NOT EXECUTED`.
- Overall phase verdict: **PARTIAL** (source/runtime blockers repaired; live-auth acceptance intentionally withheld). Detailed report: `docs/ai/phase-reports/AUTH-BROWSER-11_P0_RUNTIME_BLOCKER_REMEDIATION.md`.

## CONSOLE-01 Single-Page AI Auto Login Console (2026-10-17)

- At CONSOLE-01 time, `/` rendered the single-page `AutoLoginConsole` while the legacy account wrapper remained at `/legacy/accounts`; CONSOLE-03 removed both legacy routes and their UI graph.
- ChatGPT is the only supported console provider. Claude, Gemini, and OpenRouter are visible as `PLANNED / UNSUPPORTED` and are rejected before orchestration.
- One-shot input uses strict escaped credential parsing and `EphemeralCredentialContext`; it clears the input/context and does not write SQLite, AccountProfile, or Vault data. Temporary and keep-session modes use generated application-owned profile roots without reusing existing Chrome profiles.
- The console reuses `BrowserAuthOrchestrator`, official-origin policy, browser ownership, TOTP, cancellation, and human-takeover boundaries. Local mock E2E remains test-injected and cannot select the production destination.
- The bounded event stream sanitizes credential-like values and supports severity filters, copy, clear, auto-scroll, cancellation, and responsive layout.
- Fresh evidence: Debug/Release solution builds pass (`0` errors; Release has 23 existing analyzer warnings), architecture tests `8/8`, full integration tests `160/160`, and focused Console-01 tests `6/6`, all with `0` skips. Disposable runtime checks returned HTTP 200 for readiness/root/Blazor/CSS and no target listeners remain.
- Console/offline verdict: **GO**. Real ChatGPT credential submission, authenticated identity, and session persistence remain **NOT EXECUTED / WAITING FOR USER**; the previously pasted credential was not used.
- Detailed report: `docs/ai/phase-reports/CONSOLE-01_SINGLE_PAGE_AI_AUTO_LOGIN.md`.

## CONSOLE-02 Automation-First Playwright-Managed Chromium (2026-10-17)

- Preserved the CONSOLE-02 source specification at `docs/ai/requirements/FLASH_ROUTER_AUTOMATION_FIRST_IMPLEMENTATION_MASTER.md` and delivered the detailed evidence report at `docs/ai/phase-reports/CONSOLE-02_AUTOMATION_FIRST_PLAYWRIGHT_MANAGED_CHROMIUM.md`.
- `PlaywrightBrowserRuntime` now reports package/runtime readiness and resolves Chromium through `Playwright.CreateAsync()` and `Chromium.ExecutablePath`; the automation session launches headed Chromium with `ChromiumSandbox = true` and no system Chrome `ExecutablePath` fallback.
- Microsoft.Playwright is `1.63.0`; package metadata selects Chromium revision `1243`. The rendered console observed `READY`, revision `1243`, cache `ms-playwright`; existing cache revisions were not deleted.
- `BrowserProfileUiPort` compatibility constructors remain available, while `ConnectionsModuleRegistration` uses an explicit factory for the canonical `IBrowserProfileManager` + `IChromeProcessLauncher` path. Full host DI validation uses `ValidateOnBuild=true` and `ValidateScopes=true`.
- Added a managed-runtime regression test proving the resolved executable is under the Playwright cache, matches the expected package revision, and is not system Chrome. A stale output-directory failure was fixed by rebuilding the test project so `.playwright/package/cli.js` and `playwright.ps1` are copied correctly.
- Final validation after clean rebuild: Debug and Release builds `0 errors / 75 warnings`; architecture tests `8/8` in both configurations; serial integration tests `161/161` in both configurations with `0` skips; CONSOLE-01 focused tests `7/7`.
- Disposable Integrated host `http://127.0.0.1:5297` returned `200` for `/health/ready`, `/`, `/connections`, `/_framework/blazor.web.js`, and `17/17` discovered CSS assets. Visual Studio `http` profile checks returned `200` on `localhost:5270`; `https` profile checks returned `200` on `https://localhost:7146` and its HTTP companion. Browser inspection showed the real AI Auto Login Console and runtime `READY`; no credential was entered.
- HuflitRegistrationTool concurrency is **NOT VERIFIED** because the tool was not inspected or touched. Real credential submission, authenticated identity, and session reopen remain **NOT EXECUTED / WAITING FOR USER**. Existing `.local` data/security/profile/backup roots were preserved.

## CONSOLE-03 Legacy Hard Removal and Single-Page Console (2026-10-17)

- Removed the Account Manager legacy graph from source: `/legacy/accounts`, `/accounts`, `LegacyAccounts`, `MainWorkspace`, `BrowserAuthDialog`, Header/Filter/NavMenu, Accounts feature Razor UI/dialogs, isolated CSS, and dead template pages are gone. The root `/` still renders `AutoLoginConsole`; no legacy footer link remains.
- Accounts backend infrastructure is preserved: contracts, validation, SQLite commands/queries, model contributor, import/export integration, compatibility, Vault/security, browser automation, and existing data roots were not changed. Accounts is documented as backend-only until a future product surface is approved.
- `ModuleAssemblyCatalog` and `App.razor` no longer discover or emit Accounts UI assets. `ArchitectureRulesTests.LegacyAccountManagerUiIsHardRemovedFromTheRouteGraph` asserts no legacy/accounts or accounts Razor route and requires `Home.razor` to render the console.
- Final validation: Debug/Release builds `0 warnings / 0 errors`; architecture `9/9` both; integration `161/161` both; headed synthetic Playwright `13/13`; Console focus `7/7`; host DI composition `1/1`.
- Disposable Release runtime `http://127.0.0.1:5295` returned readiness/root/Blazor `200`, `/legacy/accounts` and `/accounts` `404`, and `16/16` CSS assets `200`; no target listener remains. CUA measured the console at `2560x1440` with no horizontal overflow and no legacy text.
- Real ChatGPT authentication, live credentials, Vault unlock, provider APIs, cookies/tokens, existing SQLite/Vault/DPAPI data, backups, and existing browser profiles were not accessed. Live-auth and session-reopen status remains **WAITING FOR USER / NOT EXECUTED**.
- Detailed report: `docs/ai/phase-reports/CONSOLE-03_LEGACY_HARD_REMOVAL_SINGLE_PAGE_REDESIGN.md`. Overall CONSOLE-03 status: **PARTIAL** (offline/source/runtime work GO; live-auth intentionally unexecuted).
