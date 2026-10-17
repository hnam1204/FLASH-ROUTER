# SIDEBAR-01 — REAL-DATA SINGLE-PAGE WORKSPACES

Date: 2026-10-17  
Repository: `hnam1204/FLASH-ROUTER`  
Implementation branch: `feat/sidebar-real-data-workspaces-20261017`  
Status: **PARTIAL — source changes implemented; Windows runtime tests not run**

## Scope

The most recent user request supersedes the older CONSOLE-04 specification prohibiting a sidebar. The current product design has a sidebar and account inspector. Implement functional sidebar workspaces inside the existing main route `/`, rather than recreating a legacy account manager or adding placeholder pages.

## Source audit

- `AppSidebar` raises `OnTabSelected`; previously `HandleTabSelected` set only `_activeTab`, while the main panel always displayed accounts.
- The account reader is `IAccountWorkspaceReader`, backed by SQLite in Integrated mode. `AccountListItemVm` has account status and tracking date but **no verified web-session expiry or MFA flag**.
- `IDiagnosticsUiPort` retrieves host, SQLite, Vault and mode diagnostics.
- `IPlaywrightBrowserRuntime` returns Chromium readiness and revisions.
- `IConsoleProviderRegistry` advertises one currently supported browser adapter (ChatGPT); other listed web login adapters are unsupported.
- `IActivityUiPort` currently resolves to `PreviewActivityUiPort` and fabricates events. It cannot be used as a production audit log.
- `ISettingsUiPort` currently resolves to `PreviewSettingsUiPort` and is not a durable configuration store.
- There is no real members / invitations / role-based access management subsystem in this repository.

## Implemented frontend slice

- `SidebarWorkspace.razor` handles **overview**, **automation**, **activity**, **configuration**, **members**, **reports**, **notifications** and **settings**; `accounts` preserves the existing account table and inspector.
- Sidebar and topbar buttons change the main view on the same `/` route; no legacy routes restored.
- Overview: account counts, stored statuses and diagnostics from existing ports.
- Automation: managed Chromium readiness, provider adapter capability list, selected account open/relogin through existing callbacks. No API/session-token replay.
- Activity: explicit lack of real audit log; lists account tracking dates as tracking dates, **not** login events.
- Configuration: genuine diagnostics; module registrations are clearly described as registrations, not runtime-health proof.
- Members: transparent backend-unavailable state; no fake people/permissions.
- Reports: provider and stored-status aggregations over real loaded account records, no invented quota or subscription usage.
- Notifications: derived attention items based on `Unverified`, `ReauthNeeded`, `ConnectionError`, `Failed` status — not falsely described as persisted/read notifications.
- Settings: working existing theme toggle; preview-only settings are not advertised as durable.
- Refresh re-reads diagnostics and account records; exceptions produce an explicit unavailable-data banner rather than a fictitious zero-record conclusion.
- Account table no longer invents remaining-session-days based on hash codes or 2FA status based on hash/tag guesses.
- Account inspector no longer invents "12 days", "80%", "2FA enabled", fallback subscription plans, default notes or successful-login events.
- KPI no longer hardcodes `+12%` monthly growth.
- Topbar no longer displays a fabricated admin identity, and notification/help buttons now navigate to the relevant workspaces.
- UI interprets `BrowserSessionObserved` and non-null observation rather than generic `IsSuccess` as login-flow success, and reports the result as an observed browser state rather than verified OAuth identity.
- Login Again actions now require a separate in-app checkbox confirming that the account is authorized and exposed credentials have been rotated. The former unconditional `credentialRotationAttested: true` call has been removed from the browser workflow call; the boolean is passed from the explicit confirmation flow.

## Gaps NOT solved in this slice

The following need separate domain-level work, not fake frontend placeholders:

1. **Durable activity journal**: append-only redacted audit records, correlation IDs, retention and account-level access.
2. **Member management**: local identity/RBAC, invite lifecycle, access tests and security review; do not infer "Admin" from a design screenshot.
3. **Durable settings**: existing `ISettingsUiPort` is Preview; design SQLite-backed settings or other approved storage with migration and confidentiality controls.
4. **Notification center**: persistence, unread/read, deduplication, scheduling and verified recipients.
5. **Automation history**: approved auth-event persistence, lifecycle events and multi-browser/profile locking; no credentials in traces.
6. **Provider usage/quota/billing**: separate approved official provider APIs only. No copied ChatGPT browser cookies, Bearer tokens, internal endpoints or 2fa.live requests.
7. **Session validity**: a browser UI observation does not establish OAuth identity or precise expiry; implement only when supported by a trusted source.
8. **Real live login**: pending user-authorized headed browser validation; profile-in-use failure is a separate open issue (see prior AUTH-BROWSER-12 branch).

## Verification status

- GitHub source files written and branch/file comparison inspected: **YES**.
- Static source-guard tests added in `SidebarWorkspaceDataIntegrityTests.cs`: **NOT EXECUTED**.
- `dotnet restore/build/test`: **NOT RUN** here (environment lacks .NET SDK).
- Responsive headed browser run: **NOT RUN**.
- Local SQLite/vault/profile data: **NOT ACCESSED OR MODIFIED**.
- Production: **NOT DEPLOYED**.
- Existing `main` branch: **UNCHANGED**.

## Local test procedure

The Windows main workspace previously contained uncommitted `BrowserAuthOrchestrator.cs` modifications and running `FLASH_ROUTER` held Debug DLL locks. **Do not reset or stash user changes or kill browsers**. Use a separate worktree:

```powershell
cd "D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER"
git fetch origin
git worktree add -b verify-sidebar01 "..\FLASH ROUTER-SIDEBAR01" origin/feat/sidebar-real-data-workspaces-20261017
cd "..\FLASH ROUTER-SIDEBAR01"
dotnet restore '.\FLASH ROUTER.sln'
dotnet build '.\FLASH ROUTER.sln' --no-restore
dotnet test '.\tests\FlashAI.IntegrationTests\FlashAI.IntegrationTests.csproj' --no-build --filter 'FullyQualifiedName~SidebarWorkspaceDataIntegrityTests|FullyQualifiedName~Console04UiRebuildTests'
dotnet test '.\FLASH ROUTER.sln' --no-build
```

Then run the host on an unused localhost port and use a **disposable SQLite data root** to validate each sidebar view on 1440x900 and 390x844 in Dark and Light modes. Verify all actions, account errors, zero-data states and browser-profile ownership. Never run destructive tests against the user's real Vault, SQLite database or profile directories.

## Verdict

**PARTIAL.** Sidebar views and integrity corrections are implemented on a review branch. All-sidebar *production-complete* status cannot be claimed until domain backend gaps are closed and Windows tests/real data acceptance run.
