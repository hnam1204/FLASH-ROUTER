# FLASH ROUTER — ANTIGRAVITY IDE CODE · MASTER EXECUTION V2
## CONSOLE-04 — PROFESSIONAL DARK/LIGHT AI AUTO LOGIN CONSOLE
**Mục tiêu:** thực thi thay đổi mã nguồn Razor/CSS/TypeScript/DI (nếu cần) trong solution đang có; không dừng ở mockup hoặc báo cáo.

> **Lệnh ưu tiên:** Trước khi thao tác, đọc hết tài liệu `FLASH_ROUTER_ANTIGRAVITY_DARK_LIGHT_UI_MASTER_PROMPT.md` và những ảnh/HTML reference người dùng cung cấp. File này mở rộng thành các bước bắt buộc, không thay thế việc đọc source thực tế. Nếu có mâu thuẫn, ưu tiên bảo mật, tính đúng đắn runtime và chỉ dẫn người dùng mới nhất.

## 00 — ROLE, WORKSPACE, EXECUTION BOUNDARY
- Executor: **Antigravity IDE Code**. Role: Principal ASP.NET Core 8 / Blazor Interactive Server Architect, Frontend Design Systems Lead, Playwright .NET Engineer, AppSec Engineer, UI QA Lead.
- Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`.
- Solution: `FLASH ROUTER.sln`.
- Current goal: **chỉ 1 giao diện sản phẩm trên `/`** để Select AI → nhập nhanh `EMAIL|PASS|2FA` → Start → managed Chromium → login được hỗ trợ → live console → kết quả.
- Không lập lại UI Account Manager; `/legacy/accounts` và `/accounts` phải tiếp tục không phục vụ giao diện cũ.
- Historical CONSOLE-03 report: architecture 9/9, integration 161/161, Playwright 13/13, Console 7/7, legacy route 404. **Đây là số liệu lịch sử, không phải test vừa chạy.** Live ChatGPT login chưa xác minh.
- Khuôn khổ công việc: CONSOLE-04 là **UI/UX rebuild thật**, không tự ý làm Tier 5 usage, billing, quota; không làm Tier 6 GitHub/release/installer/PATH/deploy.
- Không kill process của người dùng; không commit, push, deploy khi chưa được yêu cầu.

## 01 — SOURCE OF TRUTH / READ-FIRST GATE
1. Xác định và đọc hết file reference: `FLASH_ROUTER_ANTIGRAVITY_DARK_LIGHT_UI_MASTER_PROMPT.md` (dù ở thư mục gốc hoặc `docs/design`).
2. Nếu có, đọc `FLASH_ROUTER_DARK_LIGHT_CONSOLE_REFERENCE.html`, `FLASH_ROUTER_DARK_CONSOLE_PREVIEW.png`, `FLASH_ROUTER_LIGHT_CONSOLE_PREVIEW.png` và ảnh dashboard tối người dùng đính kèm. **HTML chỉ là reference**, không nhúng iframe để thay ứng dụng.
3. Đọc source Razor đang render `/`, định tuyến `Components/Routes.razor`, `App.razor`, `Components/Console/AutoLoginConsole.razor` + CSS, `MainLayout`, theme-related scripts/styles, App service registrations và các port của browser/console.
4. Đọc tài liệu phase CONSOLE-01/02/03/04 và `docs/handover/CODEX_HANDOFF.md` nếu hiện hữu; file nào không tồn tại thì ghi thiếu, không tự chế.
5. Trích yêu cầu thành `RequirementID → source component → test → runtime evidence`, ghi trong báo cáo cuối.
6. Chạy `dotnet --info`, xác định target framework và versions; kiểm tra status/hot-reload/port ownership an toàn.
7. Chạy baseline build, targeted tests (không giết Host hiện hữu) và chụp màn hình `/` zoom 100% trước khi sửa ở desktop và mobile để chứng minh cải tiến.
**STOP:** Nếu không nhìn thấy solution/source thật, không giả vờ đã sửa; báo thiếu quyền truy cập.

## 02 — PRODUCT/UI CONTRACT: CHÍNH XÁC MỘT TRANG
**Giữ:**
- Provider selector (ChatGPT nếu có adapter hiện hữu; nền tảng khác hiện rõ capability).
- Quick input `EMAIL|PASS|2FA` (không tạo account/không mở Vault cho one-shot).
- Tuỳ chọn Temporary (mặc định) / Keep browser session (tự nguyện).
- Nút `Bắt đầu đăng nhập`, `Hủy`, `Thử lại` khi đủ điều kiện.
- Browser runtime readiness hiện thời, có thể mở chi tiết Chromium version/revision/cache.
- Live console log, các bộ lọc, auto-scroll, copy log đã làm sạch, trạng thái tiến trình và kết quả.
- Theme Dark/Light ngay header.
**Không thêm:**
- Sidebar, account list, search accounts, KPI 128 accounts, nhóm/nhãn, bulk imports, admin avatar, saved credential form, proxy, cookie export, login-on-startup, tự xoay vòng account, trang usage/quota, dashboard, các link legacy.
- Không dùng số liệu demo thay kết quả thật. Nếu chưa truy vấn được thì phải ghi `Chưa kiểm tra`/`Chưa chạy`.
**Cấu trúc:** `Header (56–68px) + compact status row (khi cần) + 2-column workspace + concise footer` trong cùng một route; desktop form 360–440px, console tự chiếm phần còn lại; small screen xếp dọc.

## 03 — REFERENCE IMAGE: CHỈ HỌC NGÔN NGỮ THỊ GIÁC
Ảnh cung cấp có navy dark, điện xanh, teal, status pill, table/terminal density, nhiều trạng thái browser. Dùng cảm hứng:
- App dark, góc bo 8–12px, viền tinh tế, elevation nhẹ, icon sắc nét, độ tương phản tốt.
- Header logo tia sét FLASH ROUTER dạng phẳng, chữ không serif, tính kỹ thuật chuyên nghiệp.
- Console như một terminal thật với dòng phân cấp, không còn card Result trắng rỗng chiếm nửa trang.
Tuyệt đối **không** phục hồi các chức năng lịch sử xuất hiện trong ảnh reference (sidebar, account table, raw TOTP, cookie saving, bulk login, auto login at launch).

## 04 — DESIGN TOKENS & THEMES
**Dark (mặc định nếu chưa có lựa chọn):**
```css
--app-bg:#081320; --app-surface:#101E30; --app-elevated:#14263A;
--app-input:#0D1D30; --app-console:#07121E; --app-border:#25364A;
--text-primary:#E7EEF8; --text-muted:#95A9C3; --text-subtle:#8297AF;
--accent-primary:#2681FF; --accent-teal:#16C5AB;
--state-success:#2DDD9B; --state-warning:#F5B94E; --state-error:#F97785;
```
**Light:**
```css
--app-bg:#EFF3F9; --app-surface:#FFFFFF; --app-elevated:#F5F8FC;
--app-input:#FFFFFF; --app-console:#102033; --app-border:#D5DEEB;
--text-primary:#192B40; --text-muted:#5A6F88; --text-subtle:#697F97;
--accent-primary:#176CE3; --accent-teal:#078F7E;
--state-success:#088863; --state-warning:#B57B15; --state-error:#C84456;
```
- Đây là **đề xuất**, có thể tinh chỉnh nhỏ theo kiểm tra thị giác và contrast. Phải dùng semantic tokens tập trung, không hardcode 2 theme khắp các component.
- Chỉ dùng một theme owner thống nhất, ví dụ `html[data-theme]`; CSS Isolation các component kế thừa biến CSS từ root.
- Trạng thái: hover, pressed, focus-visible, disabled, selected, invalid, loading, warning, success, error, console scrollbars, dropdown/select native, tooltip, modal xác nhận cũng phải được theming đúng.
- Dark Console có thể giữ dark ở Light mode nếu deliberate, nhưng cần phân tách màu text rõ.
- Theme toggler có nhãn accessible, tooltip, keyboard, aria-label, focus ring và không thay đổi Start/Cancel state.
- Chủ đề lưu **chỉ một key theme** trong localStorage nếu cần, tuyệt đối không lưu login line/password/TOTP/session ở đó. Có thể dùng file JS riêng nhỏ để set theme trước first paint, phù hợp CSP và boot order, tránh flash Light→Dark. Có thể cho người dùng chọn System nếu code nguồn hiện có hỗ trợ ổn định; mặc định Dark khi chưa chọn.
- Giải quyết Blazor prerender/hydration, interactive reconnect, reload sao cho không nhảy theme hay mất state của attempt. Không gắn global secret-bearing state vào JS.
- Respect `prefers-reduced-motion` và minimum WCAG AA contrast 4.5:1 cho text thông thường; đảm bảo focus hiển thị trong cả 2 mode.

## 05 — TYPOGRAPHY / LAYOUT / DENSITY
- Font UI: Be Vietnam Pro/Inter/Segoe UI sans-serif theo font có sẵn; không tải/copy font binaries cho người dùng. Console: Cascadia Code/Consolas/monospace.
- Header logo/title 22–28px, card heading 15–17px, body/form 14–15px, labels 13–14px, console 12–13px (cân đối thực tế); không label 8px, không `letter-spacing` quá mức.
- One primary button 44–48px cao, rõ ràng; `Install/Repair Chromium` chỉ xuất hiện khi cần, không nằm ngang hàng với Start khi Browser READY.
- Desktop `width:min(100% - fluid-gutters, 1520–1600px)`; không cố định khung app chỉ ~900px giữa màn 2560px.
- Input panel width `clamp(360px, 30%, 440px)` hoặc CSS grid phù hợp, console `minmax(0,1fr)`. Container dùng box-sizing border-box; no document-level overflow.
- 1–2-level surface hierarchy, không lồng nhiều card và không sử dụng drop-shadow đen dày/đổ lệch.
- Console panel cao theo viewport có min height hợp lý (ví dụ min(65vh,680px), tùy responsive), log scroll độc lập; Result thanh footer ~44–76px trong idle.
- Horizontal and vertical spacing theo 8px grid; margins cân xứng, không để trống phần lớn màn hình.
- Mobile 375px: single column; buttons đủ vùng chạm; long log lines wrap/bounded inner scroll, không làm body tràn.
- Status strip tối đa ba chỉ dấu thật: provider capability, Chromium runtime, active attempt. Nếu chỉ có 1 trạng thái, không tạo KPI trống.

## 06 — COMPONENT MAP (ASSESS BEFORE REFACTOR)
Audit and refactor the existing `AutoLoginConsole.razor` into understandable components **only if source benefits**:
- `ConsoleHeader.razor` — logo, readiness, theme switch.
- `AutomationInputPanel.razor` — provider, masked quick input, session mode, start/cancel.
- `BrowserRuntimeStatus.razor` — ready/error/repair expandable details.
- `LiveConsolePanel.razor` — events, filtering, autoscroll, copy.
- `AutomationResultStrip.razor` — terminal outcome and next action.
- `ThemeToggle.razor` — accessible UI.
- One central CSS theme tokens file; `.razor.css` per visual component.
Ensure proper `@key`, stable parameters, `EventCallback`, async disposal and actual DI ports. Avoid giant mixed markup with embedded browser workflow code or duplicate implementation.

## 07 — CONTROL UX SPEC
**Provider Selector**: Enabled supported provider(s) only; ChatGPT must be labeled accurately e.g. `ChatGPT · Đã kiểm thử offline` versus `Live verified` only after recorded real acceptance. Unsupported providers disabled with explanation, not mock working buttons.
**Quick input**: `EMAIL|PASS|2FA` exactly primary; mask by default; show/hide deliberate, responsive, clear; TOTP optional, parser handles `\|` and `\\`. No secrets in placeholder beyond format. Readonly input in active attempt, clear sensitive UI state on cancel/finish as appropriate.
**Session mode**: Temporary default; Keep Session explicit; clarify browser profile persists website native session data but not plaintext credential. No cookie export.
**Start**: enabled only after parse validity + provider supported + no active attempt; one user-initiated scoped authorization. When starting, button switches to loading and cannot start duplicate attempt.
**Cancel**: immediately affects current `CancellationToken`, not process of another project; report cancellation in console. Do not pretend completion before engine state terminal.
**Retry**: only after terminal state; requires fresh consent; never repeat rejected password/TOTP automatically.
**Chromium Repair**: collapsed section until missing/broken; install browser only after user approves; use compatible .NET Playwright installer and `PLAYWRIGHT_SKIP_BROWSER_GC=1` in controlled installer context; do not delete unrelated revisions.
**Unsupported challenge**: show `Cần thao tác thủ công` and instruction to act in official browser; do not implement anti-bot bypass.

## 08 — LIVE CONSOLE: ACTUAL EVENTS ONLY
- Preserve existing event source; model safe event fields `AttemptId`, `SequenceNumber`, `TimestampUtc`, `Severity`, `Stage`, `SafeMessage`, `DurationMs`, `IsTerminal`.
- `INFO` action started, `PASS` only after action succeeded, `WARN/ACTION_REQUIRED` true conditions, `ERROR` verified errors. No simulated success on first render.
- UI: top bar showing attempt and state; filter All/Info/Pass/Warn/Error; auto scroll; Clear (visual only); Copy sanitized events; event count; current stage and duration. Live list supports accessible screen reader announcements without overwhelming aria-live.
- Empty: `Sẵn sàng — nhập tài khoản rồi bấm Bắt đầu`, no fake logs. Running: timestamped colored entries with readable font and pending marker; Failed: precise safe error + action. Result strip compact when idle, detailed only on terminal state.
- Logs are received while automation runs, not buffered until completion; UI refresh through `InvokeAsync(StateHasChanged)` and correct cancellation/disposal; no unbounded event lists; enforce attempt isolation and predictable ordering.
- Include errors such as `BROWSER_MISSING`, `PROFILE_IN_USE`, `NAVIGATION_FAILED`, `WAITING_FOR_USER`, `AUTH_STATE_UNKNOWN` only when evidence exists.
- Never include raw EMAIL|PASS|TOTP, password, secret, OTP, cookies/tokens, headers, storage, raw HTML/Playwright trace containing credentials. Copies/export redacted; show masked email if identity display needed.
- Status hierarchy explicit: Browser `NotChecked/Ready/Starting/Running/Failed`; Attempt `Idle/Running/WaitingForUser/Cancelled/Completed`; Auth `NotStarted/Observed/IdentityUnverified/Verified/Failed/Unknown`. No label `VERIFIED WEB` implying successful login when only public route verified.

## 09 — ENGINE BOUNDARY & DATA SAFETY
- CONSOLE-04 is UI-centric. Reuse existing parsing, ephemeral secret source, TOTP, Playwright-managed Chromium session/profile lease and BrowserAuthOrchestrator. Source edits to engine only for demonstrable broken binding/bug, with targeted tests.
- One-shot login must not require Vault or insert SQLite AccountProfile. No forced credential persistence, no bulk or background login.
- Real site origin validation per sensitive step; CAPTCHA/passkey/SSO/email/SMS OTP/unknown require user takeover. Do not treat guest ChatGPT UI, HTTP 200, or password submit as success.
- Consent is user initiated, one-attempt, account/provider/profile-bound, expires/cancellable. Past credentials posted into conversation are compromised; no use for testing. User enters replacement only into running application after rotating password/TOTP enrollment.
- Keep `.local/data`, `.local/security`, `.local/browser-profiles`, `.local/backups`, and existing `%LOCALAPPDATA%\FlashRouter` intact. No reset/migration/cleanup of user roots.
- Do not modify HuflitRegistrationTool nor Chrome/Chromium processes of other tools. Cache binaries can be shared, not sessions.
- Never resurrect `/legacy/accounts` or `/accounts`; verify direct HTTP + Razor router resolution.

## 10 — EXACT SOURCE WORKFLOW IN ANTIGRAVITY
### Milestone M0 · Evidence baseline
1. Inspect workspace, active branch/worktree, file changes and Host process. Respect user-edited files; do not stomp concurrent work.
2. Identify current route/component/layout/CSS assets and current theme styles; identify call sites for start/cancel/progress.
3. Inspect actual rendered `/` at zoom100, capture before images dark/light if applicable, measure text sizes and widths.
4. Record builds/tests and any 38 legacy Release warnings (actual fresh warning codes, not assumed).

### Milestone M1 · Token/theme layer
5. Introduce semantic dark/light tokens, theme root attribute and accessible toggle; no browser secrets in theme JS.
6. Verify Dark first paint, Light reload persistence, reset/System handling only if implemented.
7. Test browser zoom 100/125/150, theme toggle during active synthetic attempt, after reconnect/refresh.

### Milestone M2 · Shell and input
8. Replace serif hero/title with compact developer tool header, flat logo, no sidebar.
9. Rebuild left panel. Keep exact one-shot line input, parser contract and browser session modes.
10. Make browser readiness summary truthful; hide Repair while READY; no fake KPIs.

### Milestone M3 · Console and states
11. Rewrite terminal layout to use useful height and scrollbar; limit Result idle whitespace.
12. Wire real event stream for Idle/Running/Waiting/Error/Cancel/Success; filter/copied redacted.
13. Ensure Start and Cancel still call current engine, never mock callbacks; do not lose attempt due to CSS/theme change.

### Milestone M4 · Viewport/behavior validation
14. Test 375×812, 768×1024, 1024×768, 1280×800, 1440×900, 1920×1080, 2560×1440, Dark and Light at 100% zoom, with idle/running/error/human-takeover fixtures.
15. For each viewport verify document.scrollWidth ≤ document.clientWidth, panel/card overflow, readable font, contrast, focus, disabled button behavior and independently scrolling console.
16. Take screenshot artifacts with descriptive filenames, compare to reference screenshot language, fix defects and repeat.

### Milestone M5 · Regression and handoff
17. `dotnet restore "FLASH ROUTER.sln"`; build Debug+Release; run Architecture, Integration, Console and headed Playwright tests. If binaries in use, avoid terminating user app; use `UseAppHost=false` or an isolated build output supported by repo.
18. Validate DI/Blazor Host runtime including CSS fingerprint assets, `/health/ready`, `/`, no legacy routes and no regression.
19. Verify one-shot path doesn't write SQLite/Vault, no logs reveal secrets; inspect in disposable data root.
20. Report exact PASS/PARTIAL/BLOCKED/FAIL; real login remains WAITING FOR USER if no rotated credentials + in-app consent.

## 11 — ACCEPTANCE GATES (REPORT EVIDENCE)
| Gate | Evidence required |
|---|---|
| True single-page / | Screenshot + route test + no legacy links |
| Dark default | Direct fresh-profile launch and first-paint capture |
| Light complete | Screenshot + readable native form/dialog states |
| Theme switch and persistence | Toggle without reload, refresh, reconnect, only theme preference stored |
| Layout full width | 1440/1920/2560 viewport computed widths, sensible console area |
| Typography | Computed sizes and legibility, no serif or tiny labels |
| Responsive | 375/768/1024/1280/1440/1920/2560 Dark/Light matrix |
| Accessibility | Keyboard, labels, focus-visible, 44px primary targets, reduced motion, contrast |
| Form untouched behavior | Parser/escaped pipe/TOTP optional/secret mask still works |
| Browser readiness truthful | source-bound state and correct repair gating |
| Start/Cancel real wiring | event-source code + synthetic running/cancel browser E2E |
| Live console | live event capture, order/filter/copy sanitization |
| No unsafe leakage | no password/TOTP/session tokens in UI/log/storage |
| No Legacy | `/legacy/accounts` and `/accounts` cannot render old view |
| Data preservation | real data roots left intact, tests only isolated roots |
| Regression | exact fresh build and test results, categorized warnings |
| Live ChatGPT | verified only with separately rotated user-entered credentials + specific consent; otherwise WAITING FOR USER |

## 12 — REQUIRED FILES / FINAL REPORT
Implement code in the **actual Windows repo**, not only an HTML demo. Likely touching current `AutoLoginConsole.razor`, isolated CSS and theme script, plus focused components/tests as justified. Do not mandate new file creation if existing source is already adequate.

Create: `docs/ai/phase-reports/CONSOLE-04_ANTIGRAVITY_DARK_LIGHT_UI_REBUILD.md`.
Update: `docs/handover/CODEX_HANDOFF.md` with concise verified addendum (do not overwrite concurrent work).

Report sections:
1. Full reference/requirements read status; missing files.
2. Baseline route, visuals, CSS measurements and current source inventory.
3. Before/after component architecture and every source diff.
4. Dark/Light design tokens and accessible theme behavior.
5. One-page layout design rationale, typography/spacing.
6. Input/session/Start/Cancel and optional advanced browser diagnostics.
7. Live console event contract, redaction and timing behavior.
8. Screenshot matrix (paths for all viewports/theme combinations) and defect fixes.
9. Accessibility and keyboard checks.
10. Browser readiness and Playwright ownership verified unchanged.
11. Debug/Release builds and exact warnings by code.
12. Architecture/Integration/Console/Headed tests, source of truth and runtimes.
13. `/legacy/accounts`, `/accounts` negative route tests and CSS asset links.
14. Protected data preservation, HuflitRegistrationTool isolation.
15. Offline and live acceptance each separately; user action pending.
16. Remaining defects and narrow next steps.

## FINAL EXECUTION DIRECTIVE
**Do the source-code redesign in the running FLASH ROUTER app. The user asked for a genuinely modern and readable dark/light developer console matching the visual quality of the reference, not another dark-colored wireframe. Retain the exact one-line EMAIL|PASS|2FA workflow, real managed Chromium, single-page live logging, and preserved user data. No Account Manager Legacy. No fake providers or login success. No Tier 5/6. No commit/push/deploy. Verify with headed screenshots and full regression, and report honest acceptance.**
