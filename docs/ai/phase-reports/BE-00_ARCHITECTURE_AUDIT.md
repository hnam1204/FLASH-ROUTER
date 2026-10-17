# BE-00 — Architecture Audit & Contract Freeze

Ngày kiểm tra: 2026-10-15  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`

## A. Executive Summary

- `BE-00 — GO`.
- Source thực tế giữ mô hình Modular Monolith + Feature-First + Vertical Slice; host là composition root và các module chỉ reference `FlashAI.Foundation`.
- Typed UI Ports của Accounts đã tồn tại và được `MainWorkspace` sử dụng trực tiếp; implementation hiện tại là `PreviewAccountWorkspaceService` scoped theo circuit.
- SQLite mới dừng ở technical boundary: chưa có entity model, migration, database file hoặc registration `IDbContextFactory<FlashAiDbContext>`.
- `.local/data` không có database/SQLite sidecar; `.local/security` có DPAPI key hiện hữu và đã được bảo toàn, không đọc giá trị secret.
- Không có OAuth, provider API, password, API key, TOTP seed hoặc credential persistence trong phạm vi BE-01.

BE-01 được phép bắt đầu vì audit không phát hiện database hiện hữu cần migrate hay blocker dependency. BE-01 phải tạo database mới duy nhất tại `.local/data/flashrouter.db`, không seed dữ liệu preview và không fallback sang Preview khi SQLite lỗi.

## B. Source Baseline

| Hạng mục | Kết quả xác minh |
| --- | --- |
| Solution | `FLASH ROUTER.sln` |
| Startup | `FLASH ROUTER/FLASH ROUTER.csproj` |
| Target framework | `net8.0` |
| SDK/runtime | SDK `8.0.424`, `8.0.426`, `9.0.306`; runtime ASP.NET/.NET 8 và 9 |
| EF Core | `8.0.30`, SQLite provider `8.0.30` |
| Git | Không phải Git checkout; không có `git status` khả dụng |
| Database ban đầu | Không tìm thấy `.db`, `.sqlite`, `.sqlite3`, WAL/SHM hoặc backup SQLite trong workspace |
| Local security | `.local/security/key-473e4b2c-e250-46a3-923e-89c7f836a5c9.xml` tồn tại; chỉ kiểm tra metadata |
| Restore | PASS |
| Build | PASS, 0 warning, 0 error |
| Tests | PASS: 30/30 (`8` architecture + `22` integration) |

`MASTER.md` trong workspace là tài liệu thiết kế-only ghi .NET 10, còn source và prompt backend hiện tại yêu cầu giữ `net8.0` khi SDK .NET 10 chưa có. Không nâng target framework.

## C. Dependency Graph & Ownership

- Host `FLASH ROUTER` reference `FlashAI.Foundation` và 11 module assemblies, đăng ký DI và map Razor routes.
- `FlashAI.Foundation` sở hữu technical boundaries: local paths, SQLite connection factory, EF context boundary, migration coordinator, DPAPI data protection, local binding và redaction.
- Mỗi module reference duy nhất `FlashAI.Foundation`; không có module-to-module project reference và không có vòng dependency.
- Accounts sở hữu account profile, tag, favorite, archive và CRUD contract.
- Connections sở hữu provider authorization metadata; Subscriptions sở hữu observation; Authenticator sở hữu credential metadata/vault boundary; các module còn lại giữ ownership như `docs/architecture/data-ownership.md`.
- Backend persistence composition sẽ được mở rộng qua model-contributor contract trong Foundation. Module vẫn sở hữu entity/mapping của mình; migration assembly duy nhất nằm ở host composition root.

## D. Typed Port Audit

| Port | Method surface thực tế | Implementation hiện tại |
| --- | --- | --- |
| `IAccountWorkspaceReader` | `GetAccountsAsync(search, filter, sort, ct)`, `GetAccountDetailAsync(accountId, ct)` | `PreviewAccountWorkspaceService` |
| `IAccountCommandPort` | Create, update, favorite toggle, archive, restore, delete; đều trả `OperationResultVm` | `PreviewAccountWorkspaceService` |
| `IConnectionUiPort` | Status, connect, reconnect, disconnect | `PreviewConnectionUiPort` |
| `ISubscriptionUiPort` | Get observation, record manual observation | `PreviewSubscriptionUiPort` |
| `IAuthenticatorUiPort` | Lock/unlock vault, credential list/code, add/remove credential | `PreviewAuthenticatorUiPort` |
| `IImportExportUiPort` | CSV preview, commit, safe export | `PreviewImportExportUiPort` |
| `IReminderUiPort` | List, create, complete toggle, delete | `PreviewReminderUiPort` |
| `IActivityUiPort` | Read activity events | `PreviewActivityUiPort` |
| `ISettingsUiPort` | Read/save settings | `PreviewSettingsUiPort` |
| `IDiagnosticsUiPort` | Read redacted diagnostics | `PreviewDiagnosticsUiPort` |

Accounts contracts use `CancellationToken` and `OperationResultVm` but did not previously carry a concurrency token. BE-01 adds nullable `Version` properties compatibly to the Accounts ViewModels; existing call sites remain valid and the UI copies the detail version into edit commands.

The handoff document describes older method names in places (`GetConnectionStatusAsync`, `InitiateConnectionAsync`, etc.). The compilable source signatures above are authoritative; BE-01 does not rename those ports.

## E. Accounts Feature Ownership

| Feature | Existing UI | Existing app/domain/persistence | BE-01 responsibility |
| --- | --- | --- | --- |
| BrowseAccounts | `Features/BrowseAccounts/UI/AccountList*.razor` | Preview reader, `Domain/AccountRecord`, empty persistence boundary | SQL query, search, filter, stable sort, tag-aware filtering |
| AccountDetails | `Features/AccountDetails/UI/*` | Preview reader and Account ViewModels | SQL detail projection without secrets |
| CreateAccount | `Features/CreateAccount/UI/CreateAccountDialog.razor` | Preview command path | Validation, transaction, composite duplicate rule |
| EditAccount | `Features/EditAccount/UI/UnsavedChangesDialog.razor` plus host edit flow | Preview command path | Validation, update timestamp, compare-and-swap version |
| FavoriteAccount | Favorite action in list/detail UI | Preview command path | Idempotent persisted toggle |
| ArchiveAccount | `Features/ArchiveAccount/UI/ArchiveAccountDialog.razor` | Preview command path | Soft archive/restore and archived timestamp |
| DeleteAccount | `Features/DeleteAccount/UI/DeleteAccountDialog.razor` | Preview command path | Explicit hard-delete policy for a user-confirmed record; cascade owned metadata |

The current UI has no archived-list/restore affordance. The public restore command is retained and tested; adding a new layout or redesign is outside BE-01. This gap is recorded for the next UI contract iteration.

## F. Provider Model Decision

- `AccountRecord` remains a user-managed profile and is not evidence of login, subscription, quota or authorization.
- Account profiles contain no password, OAuth token, API key, cookie or TOTP seed.
- A metadata-only `ProviderConnectionRecord` shape is prepared in the Connections module with `ConnectionKind` values `WebOAuth`, `ApiKey`, `AdminApiKey`, and `Manual`.
- `ConnectionStatus.Authorized` is not assigned by BE-01; official authorization remains a later provider phase.
- ChatGPT web plans and OpenAI API credits remain separate concepts; subscription observations are not rewritten by Accounts persistence.

## G. SQLite Architecture Decision

- One database: `.local/data/flashrouter.db`; never under `wwwroot`, `bin`, `obj`, or another project.
- One `FlashAiDbContext`, created per operation through `IDbContextFactory<FlashAiDbContext>`; no singleton or circuit-lived context.
- One migration assembly in the host composition project, using the module model-contributor contract to keep Foundation free of private module references.
- Accounts owns `AccountProfiles` and `AccountTags`; Connections owns metadata-only `ProviderConnections`.
- Foreign keys are enabled; owned tags cascade with the account. Account/profile uniqueness is `(NormalizedEmail, Provider)` rather than global email uniqueness.
- UTC timestamps, an integer `Version` concurrency token, stable ordering, indexes for normalized search/filter fields, SQLite WAL and busy timeout are configured at the technical boundary.
- Integrated mode runs migrations only for the explicitly configured integrated profile. Preview mode remains in-memory and does not touch SQLite. Integrated mode fails closed on database errors; it never falls back to Preview.

## H. Migration/Data Safety Gate

- No existing database or migration history was found, so no backup approval is required before the initial migration.
- The initial migration must contain schema only; it must not seed the 12 preview accounts.
- Production/user database mutation is not permitted by this phase. BE-01 tests use unique disposable paths below `.local/test-data`.
- Existing DPAPI files are preserved and are not part of the SQLite migration.

## I. BE-01 Implementation Plan

1. Complete the Foundation context factory and model-contributor boundary.
2. Expand the existing Accounts aggregate rather than introducing a competing account domain model.
3. Add Accounts persistence mappings, validation, query handlers/service slices, and structured result handling.
4. Add the metadata-only Connections model and mapping without implementing OAuth or credential storage.
5. Add a single initial EF migration and explicit Integrated/Preview DI composition.
6. Add SQLite-provider integration tests for CRUD, search/filter/sort, uniqueness, archive/restore, delete, concurrency, rollback, foreign keys, migration, restart-style factory recreation, and no demo seed.
7. Run host smoke tests in Integrated mode, including create/reload/restart evidence, while preserving the existing UI structure and CSS.

## J. Verdict

`BE-00 — GO`

No destructive operation has been performed. BE-01 may proceed against the new workspace-local database path described above.
