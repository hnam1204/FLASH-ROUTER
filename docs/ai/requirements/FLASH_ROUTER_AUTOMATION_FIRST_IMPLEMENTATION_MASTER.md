# FLASH ROUTER — AUTOMATION-FIRST IMPLEMENTATION MASTER
## CONSOLE-02 · Playwright-managed Chromium · EMAIL|PASS|2FA · Realtime Console

**Dùng làm yêu cầu thực thi trực tiếp cho Codex trên máy Windows có source. Không phải báo cáo code đã triển khai.**

**Workspace:** `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`  
**Solution:** `FLASH ROUTER.sln`  
**Stack:** ASP.NET Core 8, Blazor Interactive Server, Microsoft.Playwright .NET; Feature-First Modular Monolith.  
**Priority:** P0 — thực hiện đúng tài liệu `AUTOMATION-FIRST REFACTOR — PLAYWRIGHT-MANAGED CHROMIUM` do người dùng cung cấp.  
**Non-goals:** Không đụng dự án HuflitRegistrationTool; không thực hiện Tier 5 usage/quota; không triển khai Tier 6 packaging/GitHub/installer/PATH. Không commit/push/deploy.

## 0. HỢP ĐỒNG SẢN PHẨM — KHÔNG ĐƯỢC DIỄN GIẢI LỆCH

FLASH ROUTER là công cụ **Automation-First**, màn hình chính duy nhất theo thứ tự:

**Select AI Platform → Enter EMAIL|PASS|2FA → Start Automation → Playwright-managed Chromium (headed) → Supported Login Workflow → Verify Authenticated Session → Realtime Console Result.**

1. **Giữ form nhập nhanh một dòng `EMAIL|PASS|2FA` làm mặc định.** Không buộc tạo AccountProfile, tạo Vault master passphrase, mở Vault hay lưu SQLite khi đăng nhập một lần.
2. Tự động hóa là **một lần thử cụ thể**, do người dùng chủ động bắt đầu và xác nhận phạm vi điền credentials. Không chạy đăng nhập nền, hàng loạt hoặc retry mật khẩu sai.
3. Sử dụng browser binary **Playwright-managed Chromium tương thích với package Microsoft.Playwright trong chính solution**; không mặc định dùng Chrome đã cài trong Windows, không hardcode `ExecutablePath` hay tên thư mục `chromium-####`.
4. Có thể dùng chung **binary cache** `%LOCALAPPDATA%\ms-playwright` với các dự án khác, nhưng không được dùng chung browser process, context, UserDataDir, cookies, storage, credentials, phiên hay diagnostic logs.
5. **Một trang duy nhất** có Platform Selector, Input, Start/Stop/Retry/Open Browser, trạng thái Browser và console log thật từng bước; phần quản lý account cũ không còn là màn hình mặc định nhưng vẫn được bảo toàn.
6. Adapter web login riêng cho từng provider; **ChatGPT là adapter được ưu tiên nghiệm thu**, không đánh đồng sáu API adapters hiện có với khả năng browser login. Provider chưa triển khai thực phải ghi `Unsupported`/`Not verified`.
7. Kết quả `Authenticated` phải dựa trên bằng chứng phiên đã đăng nhập và không mâu thuẫn với account đang chọn; không đánh đồng với HTTP 200, mở website, điền xong mật khẩu hoặc URL redirect.
8. CAPTCHA, passkey, SMS/email OTP, device approval, security key, SSO chưa hỗ trợ được chuyển `WaitingForUser` và giữ browser để người dùng thao tác; không né, vượt hay giả mạo cơ chế bảo vệ.

## 1. SECURITY PREFLIGHT

Các mật khẩu và TOTP enrollment secret từng xuất hiện trong cuộc trò chuyện phải coi là đã bị lộ. **Tuyệt đối không lấy lại, in lại, commit, seed, log, dùng làm fixture hoặc gửi lên website thật**. Trước một lần đăng nhập live với các tài khoản đó, người dùng tự đổi mật khẩu, reset/re-enroll MFA và nhập credential mới trực tiếp vào giao diện ứng dụng.

- Mật khẩu và seed chỉ dành cho AttemptId hiện tại, không được đưa vào event DTO, result DTO, message bus, logs, stack traces, crash dumps hay query string.
- Với Blazor Server, thông tin nhập được truyền về server qua circuit: không có bảo đảm tuyệt đối xóa mọi bản sao immutable string trong .NET; tối thiểu hóa bản sao và vòng đời, không lưu sau khi attempt kết thúc.
- Không đọc cookie, localStorage, bearer/session token từ browser; không gọi API ChatGPT nội bộ không công khai. Không lấy lại secret từ Chrome password manager hoặc HuflitRegistrationTool.
- Kiểm tra official HTTPS origin **và frame origin** tại từng thời điểm điền password/TOTP; từ chối redirect lạ/HTTP downgrade/localhost mock trong Production Mode.
- Mặc định chạy trên máy Windows của người dùng. Nếu Host được chạy ở máy chủ từ xa, headed Chromium chạy trên **máy chủ**, không phải Windows client; UI phải giải thích giới hạn topology, không hứa hiển thị browser cục bộ.
- Không nới lỏng sandbox, TLS, site isolation, anti-bot restrictions hay enterprise Chrome policy để ép test PASS.

## 2. PHASE A — SOURCE/DI AUDIT + BASELINE

Trước thay đổi, đọc solution, `Directory.Packages.props`/csproj, Microsoft.Playwright version, DI registration, tests, UI, runbooks, phase reports CONSOLE-01 và AUTH-BROWSER-11.

Tìm và audit các thành phần đã có (không đoán đường dẫn nếu repo thực tế khác):

- `MainWorkspace.razor`, `BrowserAuthDialog.razor`, CSS Isolation, trang `/`, `/connections`.
- `BrowserAuthOrchestrator`, `PlaywrightBrowserAuthenticationSession`, `BrowserAuthPageStateDetector`, `BrowserAuthTimingRecorder`, `BrowserAuthCredentialReader`.
- `BrowserProfileManager`, `BrowserProfileOwnership`, `ChromeProcessLauncher`, Playwright factories, `ConnectionsModuleRegistration`.
- Vault/SQLite integration, `Rfc6238TotpGenerator`, per-account identity mapping, all E2E test fixtures.

Deliver **source map**: current class → keep/refactor/adapt, current/desired dependency graph, point where `AccountId`/Vault is made mandatory, existing browser binary resolution route.

Run baseline restore, Debug/Release build, architecture/integration/focused tests; report actual counts/warnings and failed scenarios before touching code. DI must validate `ValidateOnBuild=true`, `ValidateScopes=true`; do not reintroduce constructor ambiguity.

**Gate A:** source audit and baseline evidence complete. No speculative refactor.

## 3. PHASE B — PLAYWRIGHT-MANAGED CHROMIUM RUNTIME

**Non-negotiable:** The effective runtime uses `Microsoft.Playwright` from FLASH ROUTER itself and chooses its compatible Playwright Chromium binary by **built-in Playwright executable resolution**, not by guessing revision directories.

1. Inspect package version and generated .NET Playwright installer. Confirm installed browser in `%LOCALAPPDATA%\ms-playwright` that **matches package revision**; distinguish binary cache location from app profile dir.
2. When absent, offer `Install Chromium`/`Repair Chromium` with a user-visible prompt and accurate progress; invoke **the .NET Playwright installer associated with this build**, not an arbitrary npm Playwright install or HuflitRegistrationTool's copied browser.
3. For installer/maintenance child process, set `PLAYWRIGHT_SKIP_BROWSER_GC=1` in that subprocess environment to avoid the installer cleaning Chromium revisions used by other apps; do not modify permanent global environment settings. Do not assume this guarantees all browser cache content is immutable; verify before/after revisions.
4. Never hardcode or forcibly delete `chromium-1161`, `chromium-1228`, `chromium-1248` or unknown other revisions.
5. Launch headed by default, Playwright `Chromium.LaunchPersistentContextAsync` only when profile persistence required; if temporary, use properly isolated disposable context/data root. Do not pass `ExecutablePath` when Playwright resolves the compatible engine.
6. Keep sandbox enabled when supported and tested. No `--no-sandbox` fallback for real credentials. Never silently choose system Chrome when Chromium is unavailable.
7. Settings may include bounded `NavigationTimeout`, `SlowMo` (default minimal/zero), `Headless=false`, `SessionReuse` explicit, current browser version/runtime health. On unsupported/invalid settings fail with precise diagnostics.
8. Show selected Playwright package version, resolved Chromium revision if retrievable through supported runtime metadata, browser install readiness, and safe cache location; do not pretend a filesystem folder name proves compatibility.

**Gate B:** clean standalone Windows headed Chromium launch from the project; no system Chrome dependency; missing runtime and reinstall path tested on disposable environment; sandbox/security verified.

## 4. PHASE C — DATA ROOT & PROCESS ISOLATION

Target new app-owned paths:

- Browser binary cache: `%LOCALAPPDATA%\ms-playwright` (shared binaries only).
- App root: `%LOCALAPPDATA%\FlashRouter`.
- Browser profiles: `%LOCALAPPDATA%\FlashRouter\BrowserProfiles`.
- Diagnostic logs: `%LOCALAPPDATA%\FlashRouter\Logs` (sanitized opt-in file diagnostics only).

**Backward compatibility is mandatory.** Existing FLASH ROUTER projects may already have real `.local/data`, `.local/security`, `.local/browser-profiles`, `.local/backups`. Do not implicitly migrate, rename, merge, reset, delete or generate duplicate account bindings. Identify active data root first; preserve existing path through a compatibility setting. Offer a separately authorized migration only if required, with consistent backup, rollback plan and complete profile ownership checks; migration is not required for new one-shot runtime.

- Every AttemptId owns its own browser resources and cancellation scope. No two contexts use one persistent UserDataDir simultaneously.
- A temporary profile is generated within **app-owned temporary root**; cleanup only after verified process exit and no ownership lease, never delete user-owned profile or browser cache.
- A kept session uses isolated app-owned persistent UserDataDir with explicit opt-in. Persisting browser session state is **not** persisting text password/TOTP secret. Do not share one profile across unrelated accounts.
- Never attach to personal Chrome default or any HuflitRegistrationTool profile. Never kill all Chromium/Chrome processes or remove lock files to force start.
- If HuflitRegistrationTool installed and operator permits, test concurrent execution **without editing or disrupting it**; otherwise mark external-app concurrency `NOT VERIFIED` rather than fabricate PASS.

**Gate C:** process/profile isolation and compatibility confirmed, no change to old `.local` data.

## 5. PHASE D — FAST `EMAIL|PASS|2FA` INPUT + EPHEMERAL SECRET SOURCE

The **single-line form stays the default**, not a multiline account management table. Field `2FA` means optional **Base32 TOTP enrollment seed**, not a transient 6-digit OTP; expose a separate manual OTP challenge UI when a provider requires an email/SMS code.

Define parsing contract:

- One logical line, 2 or 3 fields. `EMAIL|PASSWORD` and `EMAIL|PASSWORD|` are allowed when TOTP absent; main hint remains `EMAIL|PASS|2FA`.
- Escaping: `\|` represents a literal `|` inside a password; `\\` represents a literal backslash. Validate illegal escapes, unclosed escape, oversized input, multi-line/bulk content, malformed email/login identifier, invalid Base32 TOTP.
- Don't trim password or alter Unicode/case. No plaintext credentials in parser failure messages. Never echo pasted line or output it in logs.
- UI initially masks the full input; optional reveal/secure separate fields and clear; no localStorage/sessionStorage/browser persistence or telemetry. Because a field contains secrets, treat copy/clipboard actions as sensitive and opt-in.
- Introduce `IAttemptCredentialSource` (or reuse suitable existing abstraction) to support an **in-memory, attempt-bound ephemeral credential source** alongside legacy Vault source. Do not create fake AccountProfile/ProviderConnection/SQLite rows for transient attempts.
- One explicit Start/Confirm for exactly one provider + attempt and permitted email/password/TOTP actions, with expiry, cancellation, origin/account binding. Consent cannot be reused by background retries or other accounts.
- Release/clear reference to sensitive material after terminal state as far as managed runtime permits. Sensitive bytes never put in console events or persisted result DTOs.

**Gate D:** parser unit matrix, no required Vault/AccountId, no SQLite write, cancellation clears attempt-owned secret references.

## 6. PHASE E — SINGLE-PAGE CONSOLE UI

Replace default main route with **AI Auto Login Console**, retaining legacy Account Manager source and data as an optional non-primary path.

**Desktop >=1024px**: left panel 35–40%, right live console 60–65%. Mobile: stacked. Breakpoints 375, 768, 1024, 1280, 1440, 1920 tested. Keep CSS Isolation by feature.

Left:
1. Provider selector with capability badges/status: `Verified Web Adapter`, `Unverified Web`, `API-only`, `Unsupported`.
2. Input `EMAIL|PASS|2FA` (masked), secure-fields alternative, format/help.
3. Mode selector `Temporary Session` (default) / `Keep Browser Session` (explicit).
4. `Start Automation`, `Stop/Cancel`, `Retry` (new consent; **never retry an invalid password automatically**), `Open Browser` when permitted.
5. Browser engine/version/revision/install status, profile mode, AttemptId safe shortened, active stage and elapsed time, result.
6. Correct error/remediation for no Chromium, unsupported provider, launch failed, missing input.

Right:
- Large dark **Realtime Console** with timestamp, severity, event code, state, elapsed durations; real events only.
- Auto-scroll toggle/pause, severity filters, copy **sanitized** logs, clear local displayed logs, bounded max lines.
- Sticky status summary with `Idle / Running / WaitingForUser / Authenticated / Failed / Cancelled / Unknown`.
- Terminal reason and next user action; optional sanitized export.

Never hide primary workflow inside account details or a modal. Remove unrelated tabs from main view without destructive deletion. All controls must trigger real actions; no inert decorative buttons.

**Gate E:** browser-rendered UI, 6 viewport tests, keyboard/accessibility, no page overflow, Start/Stop/console all wired.

## 7. PHASE F — WEB PROVIDER ADAPTERS + CHATGPT IMPLEMENTATION

Use **per-provider web login adapter** behind a shared typed abstraction. Existing OpenRouter, OpenAI Platform, Anthropic, Gemini API, DeepSeek API and Groq **API adapters do not count** as website auto-login support.

Initial acceptance target: **ChatGPT**. Others must be `Unsupported` or `Not verified` until their own real website workflow and tests exist; preserve names currently registered but do not present fake support.

For ChatGPT:
- Start at `https://chatgpt.com/` (production), detect official guest/login CTA, same-page email modal, email Continue, possible official identity redirect, password, TOTP or alternate provider challenge.
- Before every credential/TOTP fill validate current page/frame origin and scoped attempt; refuse localhost mock, unknown redirect, HTTP downgrade.
- Prefer semantic Playwright locators (`GetByRole`, `GetByLabel`, `GetByPlaceholder`) with verified visibility/enabled/uniqueness; no generated CSS selector assumptions, no blind click Google/Apple/Phone instead of email Continue.
- Bounded async wait for meaningful state change, no fixed sleeps as synchronization default, no repeated credential attempts.
- For CAPTCHA/passkey/SSO/email or SMS OTP/security key/device check/unknown, transition `WaitingForUser` and leave owned browser open. Manual code input belongs in a separate limited-lifetime control, not in third field masquerading as TOTP seed.
- Only mark `Authenticated` on adequate visible account/session evidence, and distinguish `Authenticated UI observed`, `Account identity verified`, and `Unknown`. Public ChatGPT UI may also contain chat box/sidebar; not sufficient alone.
- Respect provider terms and technical restrictions; if automated steps are rejected, stop safely rather than stealth/bypass.

**Gate F:** ChatGPT synthetic browser login transitions PASS, production official public login entry observed; real authentication remains pending until user submits rotated credentials and approves.

## 8. PHASE G — LIVE CONSOLE EVENTS (NOT STATIC TEST LOGS)

Add/reuse a **typed, redacted, bounded per-attempt event stream** from Orchestrator/Playwright → Blazor Interactive Server. Use appropriate in-process channel/callback rather than a new external service; SignalR is already part of Blazor and can be used where appropriate.

Event fields: `AttemptId`, monotonically increasing `Sequence`, `UtcTimestamp`, `Severity`, `Stage`, `EventCode`, `SafeMessage`, `DurationMs?`, `IsTerminal`. Strictly no secret fields.

Required event stages: Parse → Consent → ChromiumResolve → ChromiumInstall (only if explicitly requested) → BrowserLaunch → OfficialNavigation → LoginEntry → Email → Password → TOTP/ManualVerification → SessionObservation → Terminal.

Rules:
- Emit only **completed observed states** as PASS. For started actions emit INFO. If unknown, `Unknown`, never a fake success.
- `Channel<T>` may be used but must be bounded, ordered, scoped by AttemptId, drain/close after terminal state; UI disposal and cancellation cannot leave unbounded producers.
- Live progress arrives without refreshing page, no secret leak in filtered/copied/exported output. Distinguish user waiting from automation elapsed time.
- `Stop` cancels future credential actions; cleanup only owned context/profile. Browser remains for manual takeover where needed.

**Gate G:** during headed synthetic E2E a user visibly sees evolving console lines, cancel stops activity and one terminal event appears; inspect copied logs for secrets.

## 9. PHASE H — ROBUSTNESS / SECURITY / RUNTIME

- Classified failures: parser invalid, provider unsupported, browser install missing, browser launch failure, profile in use, official origin unexpected, email/password locator missing, invalid credential (only if supported UI evidence), MFA required, manual verification, timeout, browser closed, user cancelled, authenticated state unknown.
- No generic `LaunchFailed` without attempt-specific causal info; historical failure does not permanently block a safe new attempt.
- No DI ambiguous constructors; preserve current legacy DI compatibility via explicit factory where needed.
- Browser revision runtime update must be explicit and safe; no unapproved Playwright package upgrade.
- Stage timing measured with existing recorder where possible, do not invent SLA or per-stage data for stages not executed.
- Host must run locally in protected mode. Avoid serving one-line secret form over a public HTTP endpoint. If remote topology detected, require design review before any credential input.
- Keep previously exposed credentials out of all fixtures and live tests; require user rotation and explicit one-shot consent for live acceptance.

**Gate H:** consent expiry, duplicate attempts, wrong provider/profile, origin attack, cancellation race, renderer disconnect, no secret logging, scope-safe disposal all tested.

## 10. PHASE I — TESTING & LIVE ACCEPTANCE

**Offline gate** (can run automatically with synthetic credentials and disposable paths):

1. Baseline Debug/Release builds, architecture and integration.
2. E2E parser: escaped `|`, escaped `\\`, optional third field, invalid Base32, malformed multiline/oversize.
3. No AccountProfile creation and no Vault unlocking during one-shot mode.
4. Playwright-managed Chromium resolves its own package-compatible runtime from shared binary cache without depending on system Chrome.
5. Missing Chromium reports install/repair; installer subprocess does not remove other known cache revisions.
6. Two app-owned profiles isolated, cookie/session state not shared through tests (verify isolation behavior without dumping raw cookies/tokens).
7. Chromium sandbox and cancellation, browser process lifecycle, profile ownership.
8. Headed mock login: guest → email → password → TOTP → mock authenticated; human verification → wait; invalid password → failed; cancelled → no later credential use.
9. Console receives real ordered events and accurate terminal state; no secret values in logs/report.
10. Run Host, open main page with a real browser, check UI Start/Stop and live console updates in 375/768/1024/1280/1440/1920 widths.
11. If HuflitRegistrationTool available and external concurrent run explicitly allowed, verify both can run without shareable profiles or binary cache deletion; otherwise `NOT EXECUTED` with explanation.

**Official public site gate**: In user-started acceptance, open `https://chatgpt.com/` using the **actual Playwright-managed Chromium selected for FLASH ROUTER**, detect public guest and login/email entry without submitting credentials. Never use test localhost as production destination.

**Real login gate**: Only after user rotates previously exposed password/TOTP, enters replacements in FLASH ROUTER itself and explicitly consents. One account, one attempt, no blind retries. Separate outcomes for Email, Password, TOTP, HumanAction, Authenticated UI, Identity confirmation and persisted profile reopen. Do not claim live success without actual evidence.

**Do not double-count tests**; report exact pass/fail/skip and transient failures, Debug vs Release warnings separately. The known prior 38 Release analyzer warnings must be identified and compared rather than simply suppressed.

## 11. PHASE J — DELIVERABLES AND REPORT

Create `docs/ai/phase-reports/CONSOLE-02_AUTOMATION_FIRST_PLAYWRIGHT_MANAGED_CHROMIUM.md` and update handoff/runbook/security docs **only after real code changes**.

Required report:
1. Source baseline and actual code file/class/DI diff.
2. Product UX before → after with screenshot evidence of the **real new single-page console**.
3. Microsoft.Playwright version; compatible Chromium runtime/revision (verified using supported runtime knowledge); install/repair result; binary cache inventory before/after.
4. Verified isolation from HuflitRegistrationTool, or not-executed reason.
5. Actual active data root and proof preexisting `.local` SQLite/Vault/profiles/backups preserved.
6. Parser and ephemeral credential source security.
7. ChatGPT adapter implementation; provider support matrix (Web verified/offline only/API-only/Unsupported).
8. Console event flow with real timestamped screenshot or redacted trace; Start/Stop behavior.
9. Build and test counts, browser E2E, UI viewport checks, Debug/Release warnings and DI validation.
10. Public official navigation evidence, real credential acceptance status, authenticated session and profile reopen status.
11. Exact blockers, root causes, next user action, GO/PARTIAL/BLOCKED/FAIL; no false CERTIFIED PASS.

### Final matrix

`ONE-PAGE CONSOLE — PASS/PARTIAL/FAIL`  
`EMAIL|PASS|2FA QUICK INPUT — PASS/PARTIAL/FAIL`  
`NO MANDATORY VAULT/SQLITE — PASS/PARTIAL/FAIL`  
`PLAYWRIGHT-MANAGED CHROMIUM — PASS/PARTIAL/FAIL`  
`CHROMIUM INSTALL/REPAIR — PASS/PARTIAL/BLOCKED`  
`SHARED BINARY CACHE PRESERVED — PASS/PARTIAL/FAIL`  
`PROFILE/PROCESS ISOLATION — PASS/PARTIAL/FAIL`  
`HUFLIT TOOL UNCHANGED — VERIFIED/NOT VERIFIED`  
`CHATGPT WEB ADAPTER — PASS/PARTIAL/FAIL`  
`LIVE CONSOLE EVENTS — PASS/PARTIAL/FAIL`  
`START/STOP/CANCEL — PASS/PARTIAL/FAIL`  
`AUTOMATION SECURITY — PASS/PARTIAL/FAIL`  
`SYNTHETIC HEADED E2E — PASS/PARTIAL/FAIL`  
`OFFICIAL PUBLIC NAVIGATION — VERIFIED/NOT EXECUTED/BLOCKED`  
`REAL AUTHENTICATED SESSION — VERIFIED/NOT EXECUTED/BLOCKED`  
`REAL SESSION PERSISTENCE — VERIFIED/NOT EXECUTED/BLOCKED`  
`EXISTING USER DATA — PRESERVED/NOT VERIFIED/FAIL`  
`OVERALL OFFLINE — PASS/PARTIAL/FAIL`  
`OVERALL LIVE — PASS/PARTIAL/BLOCKED/NOT EXECUTED`

**Do not claim COMPLETE solely from HTTP 200, DI test, Chromium window, synthetic E2E or documentation.**

## 12. EXECUTION SEQUENCE — CODEX MUST ACT

1. Read source + user's new attached principles + handoff; establish baseline.
2. Audit all current launch paths and environment/data roots.
3. Implement Playwright-managed Chromium resolver/readiness/install/repair and isolate binary cache from profile storage.
4. Preserve old persistent data/profiles; use new app-specific paths without destructive migration.
5. Implement strict one-line parser and ephemeral in-memory credential source.
6. Reuse BrowserAuthOrchestrator/state detector for no-AccountId one-shot workflow; adapt consent and profile ownership.
7. Build single-page Blazor UI (selected platform + masked one-line form + Start/Stop + realtime console).
8. Connect actual typed events, timings, log redaction, cancellation and browser health.
9. Verify current ChatGPT adapter, repair missing real state detection; do not fake other provider support.
10. Run synthetic headed E2E, separate browser runtime regression, and full Debug/Release suite.
11. Run Windows local Host and verify actual new single-page UI and public official ChatGPT navigation.
12. If no rotated credentials and in-app consent, stop **only at live secret submission gate**, mark that gate waiting, do not stop unrelated engineering work.
13. Do not read, edit, launch, or disrupt HuflitRegistrationTool without explicit need/permission; if available only inspect enough for noninterference evidence.
14. Write detailed report including exact files changed and GO/no-GO matrix. Do not end with documents-only output if source code still lacks the requested flow.

**Implement directly in the user's repository on the machine where Codex has access. The attached file is the primary product specification. Preserve testability, security, compatibility and the one-line fast-entry experience.**


