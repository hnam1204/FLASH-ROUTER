# ADR-0007: FEATURE-FIRST & VERTICAL SLICE ARCHITECTURE REFACTORING

**Trạng thái:** ACCEPTED  
**Ngày quyết định:** 2026-10-15  
**Phạm vi:** Toàn bộ 11 Modules và Host Application (`FLASH ROUTER`)  
**Người quyết định:** Antigravity Solution Architecture Team  

---

## 1. Bối cảnh (Context)

Sau khi hoàn thành UI Phase A, FLASH ROUTER đã sở hữu một giao diện Single-Page Account-First hiện đại, tối giản và đáp ứng đầy đủ tương tác. Tuy nhiên, ở cấp độ tổ chức mã nguồn, các module trước đó được gom nhóm theo kiểu kỹ thuật tầng ngang (layer-centric) hoặc gom tất cả các Razor components vào một thư mục chung:
```text
Module/
├── UI/
│   └── Components/
│       ├── ComponentA.razor
│       ├── ComponentB.razor
│       └── ...
├── Application/
├── Domain/
└── Persistence/
```
Cách tổ chức này gây ra các khó khăn khi mở rộng:
1. **Khó định vị tính năng (Co-location):** Khi cần sửa hoặc phát triển một use case cụ thể (ví dụ `CreateAccount` hay `ManageSettings`), kỹ sư phải nhảy qua lại giữa nhiều thư mục cách xa nhau.
2. **Nguy cơ rò rỉ ranh giới:** Thiếu ranh giới vật lý rõ ràng giữa các use case khiến các component dễ phụ thuộc chéo vào nhau trong cùng module.
3. **Khó bàn giao cho Codex:** Đội ngũ backend Codex gặp khó khăn khi xác định chính xác vị trí cần đặt Command, Handler, Validator và Persistence logic cho từng use case cụ thể.

---

## 2. Quyết định Kiến trúc (Architectural Decision)

Chúng tôi quyết định chuẩn hóa toàn bộ source code của 11 module và Host theo mô hình **Feature-First + Vertical Slice Architecture**:

### 2.1. Cấu trúc Chuẩn hóa Cấp Module
```text
src/Modules/<ModuleName>/
├── Features/
│   ├── <FeatureA>/
│   │   ├── UI/
│   │   │   ├── <Component>.razor
│   │   │   └── <Component>.razor.css
│   │   ├── Application/
│   │   │   ├── <Command/Query>.cs
│   │   │   └── <Handler>.cs
│   │   ├── Validation/
│   │   └── Contracts/
│   └── <FeatureB>/
│       └── UI/
├── Domain/                 (Chứa Domain Aggregates/Value Objects dùng chung của module)
├── Persistence/            (Chứa Entity Mappings, Repository, DbContext Boundaries)
├── Contracts/              (Chứa Public Interfaces, DTOs, ViewModels dùng cho Inter-module DI)
├── Application/            (Chứa Module-level Orchestration, Boundaries)
├── UI/                     (Chứa Route Page tương thích module và _Imports.razor)
├── <Module>ModuleMarker.cs
├── <Module>ModuleRegistration.cs
└── README.md
```

### 2.2. Phân định Ranh giới Domain & Persistence (Rất quan trọng)
- **Tách nghiệp vụ theo Feature, KHÔNG tách Database/DbContext theo từng Feature:**
  - Toàn bộ ứng dụng sử dụng một SQLite database duy nhất tại local (`FLASH_ROUTER.db`).
  - Không tạo 11 SQLite databases, không tạo DbContext riêng cho từng nút bấm hoặc từng feature.
  - Các Feature trong cùng Module (`CreateAccount`, `EditAccount`, `DeleteAccount`) chia sẻ chung Domain Aggregate `AccountRecord` tại `Domain/` và cùng Persistence boundary tại `Persistence/`.
  - Nghiêm cấm nhân bản Domain Aggregate thành nhiều file rời rạc ở mỗi feature.

### 2.3. Ranh giới Phụ thuộc (Rule 31 & Rule 71)
- **Rule 31 (ModuleProjectsReferenceFoundationOnly):** Mỗi module `.csproj` chỉ được reference duy nhất `FlashAI.Foundation.csproj`. Tuyệt đối không reference chéo giữa các module.
- **Xử lý Settings & Diagnostics:** `SettingsDialog` được đưa về `src/Modules/Settings/Features/ManageSettings/UI/SettingsDialog.razor`. Để hiển thị thông tin chẩn đoán mà không vi phạm Rule 31, `SettingsDialog` nhận `[Parameter] public RenderFragment? DiagnosticsContent { get; set; }`. Host `MainWorkspace` đóng vai trò Composition Root, inject `DiagnosticsPanel` từ module Diagnostics vào `SettingsDialog`.
- **CSS Isolation (Rule 71):** Mỗi Razor component trong `Features/<Feature>/UI/` luôn đi kèm file `.razor.css` tương ứng. Không rò rỉ selector nghiệp vụ ra các file CSS toàn cục.

---

## 3. Hệ quả (Consequences)

### Tích cực:
1. **Định vị tức thì:** Mở Explorer có thể tìm thấy ngay chức năng cần làm việc tại `src/Modules/<Module>/Features/<Feature>/`.
2. **Chuẩn bị sẵn sàng cho Codex:** Mỗi feature đã có sẵn thư mục `UI/` và vị trí chuẩn hóa để Codex cắm `Application/` (`Command`, `Handler`), `Validation/` và `Contracts/`.
3. **An toàn kiểm thử:** 100% các bài test hiện có (22 integration tests) và toàn bộ 8 architecture tests đều PASS.
4. **Bảo toàn trải nghiệm UI:** Giao diện Single-Page Account-First, màu sắc, bố cục, responsive và các luồng preview hoạt động hoàn hảo không một lỗi hồi quy.

### Đánh đổi (Trade-offs):
- Số lượng thư mục cấp con tăng lên để phục vụ tính cô lập cao của Vertical Slice.
- Host cần quản lý `@using` rõ ràng cho các feature namespaces (đã được tập trung hóa trong `_Imports.razor`).

---

## 4. Kiểm thử Kiến trúc Tự động

Đã bổ sung 2 bài test tự động trong `FlashAI.ArchitectureTests`:
1. `AllModulesHaveFeatureFirstStructure`: Đảm bảo tất cả 11 module đều có thư mục `Features/` với các feature con có cấu trúc hợp lệ.
2. `FeatureRazorComponentsHaveIsolatedCss`: Đảm bảo tất cả component trong `Features/` đều có file CSS Isolation đi kèm.
