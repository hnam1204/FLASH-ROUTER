# FLASH ROUTER

FLASH ROUTER is a local-first Blazor application for managing user-owned AI account profiles and explicitly authorized connections.

The repository implements **BE-01 Accounts SQLite persistence**, **BE-02 local encrypted credential vault/TOTP**, **BE-03 offline official ChatGPT OAuth contracts**, **BE-04/BE-05 offline provider capability verification and sync orchestration**, and **BE-06 local encrypted backup/recovery plus Windows release certification**. The host, technical foundation, eleven feature modules, isolated route styles, module registrations, typed ports, EF migrations, SQLite integration tests, encrypted vault tests, provider fixture tests, backup/recovery tests, and local-only runbooks are present. Live OAuth and live provider verification remain `NOT VERIFIED` pending eligibility, registration, user consent, explicit credentials, and explicit live-test approval.

## Run locally

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget\packages')
dotnet restore '.\FLASH ROUTER.sln'
dotnet build '.\FLASH ROUTER.sln'
dotnet test '.\FLASH ROUTER.sln'
dotnet run --project '.\FLASH ROUTER\FLASH ROUTER.csproj' --launch-profile http
```

The development profile is localhost-only. Project-local runtime folders are excluded from Git and are created under `.local` when later phases need them.

## Phase status

The installed machine provides .NET SDK 8 and 9, not .NET 10. The solution remains on `net8.0` per the backend implementation constraint. Integrated mode stores Accounts data at `.local/data/flashrouter.db`; Preview mode is still available by explicit configuration. BE-01 is certified `GO`; BE-02 is certified `GO (offline/local security gate)`; BE-03 is `OFFLINE GO`; official OAuth live capability is `BLOCKED` and `LIVE NOT VERIFIED`; BE-04/BE-05 are `OFFLINE GO`; and BE-06 is an `OFFLINE RELEASE CANDIDATE` for encrypted backup/recovery, local browser smoke, and the self-contained Windows artifact. The five viewport captures were run against the local release host; the in-app CUA smoke verified `/`, `/connections`, and `/backups`. No live provider calls, credentials, billable operations, or active user data were used. Package vulnerabilities remain an open hardening finding, so BE-06 is not security-clean.

The certified Windows artifact is `.local/releases/BE-06/win-x64-verified-3`; its manifest and checksums are under `.local/releases/BE-06/`. See `docs/ai/phase-reports/BE-00_ARCHITECTURE_AUDIT.md`, `docs/ai/phase-reports/BE-01_SQLITE_PERSISTENCE.md`, `docs/ai/phase-reports/BE-02_VAULT_CERTIFICATION.md`, `docs/ai/phase-reports/BE-03_OFFICIAL_OAUTH.md`, `docs/ai/phase-reports/BE-06_FINAL_REPORT.md`, and `docs/runbooks/development.md` for evidence and the local workflow.

BE-07 adds controlled provider readiness plans, .NET 10 upgrade readiness, data-accuracy findings, and a current-source artifact at `.local/releases/BE-07/win-x64-verified-3`. Live provider/OAuth verification remains unexecuted. The latest-observation retention regression is fixed and covered by offline tests; full data-accuracy certification remains open.

CLI-01 adds a self-contained Windows `flashrouter` launcher and versioned package at `.local/releases/CLI-01/win-x64/package-release-v4`. It supports managed start/open/status/stop/restart/logs/version/help, preserves the configured SQLite/Vault root, and includes a sandbox-only installer script. Real per-user installation and User PATH mutation remain unauthorized until explicitly approved. See `docs/runbooks/windows-cli.md` and `docs/ai/phase-reports/CLI-01_WINDOWS_COMMAND_LAUNCHER.md`.
