# FLASH ROUTER - UI-C Account Connection UX Correction Report

Date: 2026-10-16

## Executive result

UI-C đã hoàn tất trong phạm vi offline/local được phép. Giao diện account workspace đã chuyển sang luồng account-first, có trạng thái rỗng trung thực, bộ lọc rút gọn, hai phương thức kết nối rõ ràng và persistence tích hợp với SQLite + Vault mã hóa. Không thực hiện OAuth live, đăng nhập credential thật hoặc trích xuất cookie/localStorage.

Overall: `PARTIAL`.

Lý do giữ `PARTIAL`: các kiểm thử tự động và kiểm tra runtime offline đều đạt, nhưng ma trận responsive đầy đủ theo các kích thước yêu cầu chưa được hoàn tất trong browser hiện tại.

## Verdict matrix

| Gate | Verdict | Evidence / limitation |
| --- | --- | --- |
| UI-C0 | `GO` | Header không còn Settings; UI chính chỉ còn bốn filter: Tất cả, Đã kết nối, Chưa xác minh, Cần chú ý. |
| UI-C1 | `GO` | Account list có empty state cho chưa có tài khoản, không tìm thấy tài khoản và lỗi backend; heading search-empty là `Không tìm thấy tài khoản`. |
| UI-C2 | `GO` | Add Account chỉ expose OAuth chính thức và credential + TOTP tùy chọn; không còn Favorite/Tags control trong primary account UI. |
| UI-C3 | `GO` | Import/export dùng SQLite, CSV parser xử lý quoted fields và blank input, diagnostics đọc runtime state. |
| UI-C4 OAuth Offline | `GO / BLOCKED` | Contract, eligibility gate, callback/security primitives và provider-subject handling được test offline; trạng thái live bị chặn nếu chưa xác nhận eligibility. |
| UI-C4 OAuth Live | `NOT EXECUTED / BLOCKED` | Không gọi provider thật và không dùng client registration, token, tài khoản hoặc credential thật. |
| UI-C5 | `GO` | Password/TOTP được lưu trong Vault mã hóa; SQLite chỉ giữ metadata/reference; commit SQLite lỗi sẽ cleanup secret đã tạo. |
| UI-C6 | `GO WITH LIMITATION` | Automated tests pass và runtime smoke checks pass; responsive matrix đầy đủ chưa hoàn tất. |

## Implemented scope

- `MainWorkspace` chuyển sang integrated SQLite state khi cấu hình `FlashAI:AccountStorageMode=Integrated`, tính badge counts từ dữ liệu thực và không hiển thị dữ liệu demo trong integrated mode.
- `AccountList` cung cấp loading, backend-error, no-account và search/filter-empty states với hành động recovery phù hợp.
- `CreateAccountDialog` phân biệt OAuth chính thức với credential flow; OAuth action bị disable khi eligibility chưa được xác nhận và copy không tuyên bố login thành công chỉ vì mở browser.
- `SqliteAccountCredentialUiPort` ghi secret vào Vault trước, ghi reference/metadata vào SQLite sau, và dọn secret nếu commit SQLite thất bại.
- OAuth UI port không nhận email làm provider subject; provider subject chỉ đến từ identity đã được xác minh trong callback flow.
- Integrated import/export và runtime diagnostics không dùng preview-only state khi app chạy ở integrated mode.
- Favorite/Tags fields, SQLite schema và APIs được giữ để tương thích dữ liệu; các control tương ứng đã loại khỏi primary account UI.
- Selector CSS Favorite/Tags không còn sử dụng cũng đã được dọn khỏi account list.

## Verification

Commands executed from the solution root:

```text
dotnet build "FLASH ROUTER.sln" -c Debug --no-restore
dotnet test "tests\\FlashAI.IntegrationTests\\FlashAI.IntegrationTests.csproj" -c Debug --no-build --no-restore --filter "FullyQualifiedName~UiCAccountConnectionTests"
dotnet test "tests\\FlashAI.IntegrationTests\\FlashAI.IntegrationTests.csproj" -c Debug --no-build --no-restore
dotnet test "tests\\FlashAI.ArchitectureTests\\FlashAI.ArchitectureTests.csproj" -c Debug --no-build --no-restore
```

Results:

- Build: final Debug build `0 errors`; full compilation has only pre-existing analyzer warnings.
- Focused UI-C: `5 passed, 0 failed, 0 skipped`.
- Full integration: `80 passed, 0 failed, 0 skipped`.
- Architecture: `8 passed, 0 failed, 0 skipped`.
- Combined test total: `88 passed, 0 failed, 0 skipped`.
- Runtime smoke verification: `/` and `/diagnostics` returned HTTP 200; integrated SQLite empty state, four filters, Add Account methods, OAuth blocked state, credential fields and empty/search states were observed.

## Security and test boundaries

- No plaintext password or TOTP is written to SQLite, source, logs or browser fields beyond the active form binding.
- No live OAuth, provider verification, bulk login checking, cookie extraction or localStorage extraction was performed.
- Existing `.local/data`, `.local/security` and `.local/backups` were preserved.
- The user application process was not terminated.

## Follow-up

1. Complete the browser responsive matrix at 375, 768, 1024, 1280 and 1440 px.
2. Keep OAuth live blocked until provider eligibility, registration, consent and explicit live-test approval are available.
3. Revisit the existing analyzer warnings separately from UI-C acceptance.
