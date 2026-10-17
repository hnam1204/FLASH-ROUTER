# BE-01 — SQLite Persistence & Integrated Mode Report

Ngày kiểm tra: 2026-10-15  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`  
Solution: `FLASH ROUTER.sln`  
Target framework: `net8.0`

## A. Executive Summary

- `BE-00 — GO` đã được ghi nhận trong `BE-00_ARCHITECTURE_AUDIT.md`.
- BE-01 đã triển khai persistence thật cho module Accounts trên SQLite, giữ nguyên typed UI ports và không thay đổi layout/CSS của One-Page workspace.
- Preview và Integrated là hai composition rõ ràng. Integrated không seed 12 tài khoản demo và không fallback tự động sang Preview khi database lỗi.
- Database runtime là `.local/data/flashrouter.db`; migration assembly duy nhất là host `FLASH ROUTER`.
- Backend/database evidence đạt: build pass, architecture tests `8/8`, SQLite integration tests `29/29`, migration runtime pass, restart pass, integrity check `ok`, database sau cleanup có `0` account.
- Browser persistence scenario đạt: create → reload → restart → update. Responsive/visual regression matrix đạt tại `375x812`, `768x900`, `1024x900`, `1280x900` và `1440x900`.
- Trong quá trình certification đã bổ sung khả năng khôi phục tài khoản từ archived list/context menu và detail panel, dùng lại `RestoreAccountAsync` hiện hữu.

## B. Source Baseline

| Hạng mục | Kết quả |
| --- | --- |
| SDK | .NET SDK 8 và 9 hiện có trên máy |
| Framework | `net8.0`, giữ nguyên theo constraint BE-01 |
| Git | Workspace không phải Git checkout |
| Database ban đầu | Không có SQLite database trước BE-01 |
| Security material | `.local/security/key-473e4b2c-e250-46a3-923e-89c7f836a5c9.xml` được bảo toàn |
| Existing baseline | Build và 30 test baseline đã pass trước khi BE-01 thay đổi |

## C. SQLite Design

### Database and composition

- Path: `.local/data/flashrouter.db`.
- `IDbContextFactory<FlashAiDbContext>` tạo context ngắn hạn cho từng operation; không dùng singleton DbContext.
- `MigrationCoordinator` là pipeline duy nhất và chỉ chạy khi `FlashAI:AccountStorageMode=Integrated`.
- `SqliteConnectionFactory` bật WAL, `foreign_keys=ON`, busy timeout và thư mục dữ liệu local.
- `SqliteRetryPolicy` là retry boundary cho lỗi SQLite tạm thời.

### Tables and constraints

- `AccountProfiles`: profile metadata, normalized search columns, favorite/archive state, UTC timestamps và integer `Version` concurrency token.
- `AccountTags`: khóa ghép `(AccountId, Value)`, foreign key cascade về Accounts.
- `ProviderConnections`: metadata-only Web/API connection records, foreign key tới `AccountProfiles`, không có password/token/secret columns.
- `__EFMigrationsHistory`: lịch sử migration EF Core.
- Unique account rule: `(NormalizedEmail, NormalizedProvider)`.
- Indexes: numeric ID, normalized email/provider, display name, archive/tracking date và tag value.

### Migrations

1. `20261015114456_InitialAccountPersistence`
2. `20261015115755_AddProviderConnectionAccountForeignKey`
3. `20261015120227_AddTrackingDateTicksUtc`

Runtime lần đầu đã áp dụng migration 2 và 3 lên database workspace. Lần restart tiếp theo ghi nhận `No migrations were applied. The database is already up to date.` Không drop database, không truncate dữ liệu hiện hữu, không xóa migration history.

## D. Feature Implementation

| Feature | Application | Domain | Persistence | Contract | Tests | Runtime |
| --- | --- | --- | --- | --- | --- | --- |
| BrowseAccounts | `SqliteAccountWorkspaceReader` | `AccountRecord` | LINQ/EF query, stable sort, filters, tag search | `IAccountWorkspaceReader` | search/filter/sort/persistence | PASS |
| AccountDetails | `GetAccountDetailAsync` | `AccountRecord` | projection mapper, no secrets | `AccountDetailVm` | read after recreation/restart | PASS |
| CreateAccount | `SqliteAccountCommandPort.CreateAccountAsync` | validation + aggregate | transaction, duplicate constraint | `IAccountCommandPort` | create, invalid input, duplicate | PASS |
| EditAccount | `UpdateAccountAsync` | profile update/version | compare-and-swap `Version` | `AccountEditorVm` | stale writer conflict | PASS |
| FavoriteAccount | `ToggleFavoriteAsync` | favorite transition | persisted boolean/version | command port | durable toggle | PASS |
| Archive/Restore | archive/restore commands | lifecycle state | `IsArchived`, `ArchivedAtUtc` | command port | archive, restore | PASS |
| DeleteAccount | explicit existing UI confirmation | owned record deletion | cascade owned tags | command port | delete/detail absence | PASS |
| Provider metadata | Connections model contributor | `ProviderConnectionRecord` | FK and metadata-only schema | Connections domain | FK and no-secret columns | PASS |

## E. File Changes

### Added or extended implementation

- `src/FlashAI.Foundation/Persistence/FlashAiDbContext.cs`
- `src/FlashAI.Foundation/Persistence/IFlashAiModelContributor.cs`
- `src/FlashAI.Foundation/Persistence/LocalDataPaths.cs`
- `src/FlashAI.Foundation/Persistence/MigrationCoordinator.cs`
- `src/FlashAI.Foundation/Persistence/SqliteConnectionFactory.cs`
- `src/FlashAI.Foundation/Persistence/SqliteRetryPolicy.cs`
- `src/Modules/Accounts/Application/SqliteAccountCommandPort.cs`
- `src/Modules/Accounts/Features/BrowseAccounts/Application/SqliteAccountWorkspaceReader.cs`
- `src/Modules/Accounts/Persistence/AccountModelContributor.cs`
- `src/Modules/Accounts/Persistence/AccountProjectionMapper.cs`
- `FLASH ROUTER/Persistence/FlashAiDesignTimeDbContextFactory.cs`
- `FLASH ROUTER/Persistence/Migrations/*`
- `tests/FlashAI.IntegrationTests/SqliteAccountPersistenceTests.cs`

### Modified integration and documentation

- `FLASH ROUTER/Program.cs`: host migration execution in Integrated mode.
- `FLASH ROUTER/Components/Workspace/Main/MainWorkspace.razor`: hides Preview banner in Integrated mode and checks command results before success toasts.
- `src/Modules/Accounts/AccountsModuleRegistration.cs`: explicit Preview/Integrated DI selection.
- `.gitignore`: explicit SQLite and disposable test-data patterns.
- `README.md`, `src/Modules/Accounts/README.md`, `docs/architecture/ADR-0003-sqlite.md`, `docs/architecture/module-registry.md`, `docs/architecture/data-ownership.md`, `docs/handover/CODEX_HANDOFF.md`, `docs/runbooks/development.md`.

### Preserved

- `.local/security/key-473e4b2c-e250-46a3-923e-89c7f836a5c9.xml`.
- Existing One-Page Razor layout, feature-first structure, isolated CSS and typed UI contracts.
- No OAuth implementation, live provider calls, API keys, passwords, tokens, cookies or TOTP seed storage.

## F. Test Evidence

### Automated

Command:

```powershell
dotnet build "FLASH ROUTER.sln"
dotnet test "FLASH ROUTER.sln" --no-restore
```

Results:

- Architecture: `8 passed, 0 failed`.
- Integration: `29 passed, 0 failed`.
- Build: `0` warnings, `0` errors on the final serial build.
- Disposable test databases: unique `.local/test-data/be01-*` paths; cleanup succeeds after clearing SQLite pools.

Coverage includes create, read, search, Unicode/tag search, stable sort, update, duplicate detection, favorite, archive, restore, delete, invalid input, optimistic concurrency, foreign keys, unique constraints, migration schema, context recreation and no-preview-seed behavior.

### Runtime database inspection

After Integrated startup and cleanup of the synthetic browser test record:

| Check | Result |
| --- | --- |
| Tables | `AccountProfiles`, `AccountTags`, `ProviderConnections`, `__EFMigrationsHistory` |
| Migration history | All 3 BE-01 migrations present, EF Core `8.0.30` |
| Account count | `0` |
| Provider connection count | `0` |
| Foreign keys | `1` |
| Journal mode | `wal` |
| Integrity check | `ok` |
| Database file | Preserved; not recreated or deleted |

## G. Browser Persistence Evidence

Integrated app was launched at `http://localhost:5270` with `AccountStorageMode=Integrated`.

1. Initial screen showed `0` accounts and no `DEMO DATA — DEVELOPMENT PREVIEW` banner.
2. Created synthetic `be01.runtime@example.test` through the existing Create dialog.
3. Account appeared with `BE-01 Runtime Check` and count `1`.
4. Browser reload retained the account.
5. Application process was stopped and restarted; host reported database already up to date.
6. Browser reload after restart retained the account.
7. Display name was changed to `BE-01 Runtime Updated` and saved.
8. Updated value remained after save and after the restart reload.
9. The synthetic row was removed by an exact parameterized email cleanup; final database count is `0`.

Responsive and visual regression was verified at `375x812`, `768x900`, `1024x900`, `1280x900` and `1440x900`. Không có document-level horizontal overflow; mobile back navigation, split workspace, internal filter scrolling, focus traversal, search/empty state, validation, edit, favorite, archive/restore, theme và privacy masking đều đạt. Layout tại mobile đã được giới hạn để filter rail chỉ scroll nội bộ.

## H. Security Evidence

- No credential/token/password/secret columns in `ProviderConnections`.
- Account projection returns metadata only; no secrets are accepted by BE-01 validation.
- Database queries use EF parameterization; no concatenated user SQL in application paths.
- DPAPI security file was not changed or deleted.
- Localhost-only launch profile remained in use.
- Integrated mode does not display the Preview/DEMO banner and does not silently switch to Preview.

## I. Remaining Work

- BE-02 encrypted vault and production secret handling.
- BE-03 API provider adapters.
- BE-04 official Web OAuth.
- BE-05 usage/quota scheduler.
- BE-06 production hardening, backup/recovery and broader module persistence.

## J. Final Verdict

`BE-00 — GO`

`BE-01 — GO`

The Accounts SQLite implementation, runtime persistence evidence and required browser regression matrix pass. This is a phase gate result; it is not a production-ready claim and does not include live OAuth/provider integrations or credential vault storage.
