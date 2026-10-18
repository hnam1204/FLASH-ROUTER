# V4.5.1 Runtime Baseline

Date: 2026-10-18
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Source

- Branch: `feat/account-automation-v4-4-recovery`
- HEAD: `8a115c72360e5e2bd9bb956d347dfd726a3bb6fc`
- Solution project count: 9
- Existing V4.5.1 modifications: preserved; no reset, clean, commit, merge, push, or deployment was performed.
- `git diff --check`: completed without whitespace errors. Git emitted only normal LF/CRLF conversion warnings for existing modified files.

## Runtime reconciliation

- The stale `.local/runtime/instance.json` referenced PID `35940` and port `5270`; no matching process or listener existed during the final check.
- A disposable integrated host was launched on `http://127.0.0.1:5271` with data root `.local/temp/v451-disposable-root`.
- The disposable host applied migrations to an empty database, rendered the current console, and was stopped gracefully.
- No canonical application process was stopped. No global `dotnet.exe` or Chrome process termination was used.

## Tooling

- Active SDK: `9.0.306`
- Installed SDKs: `8.0.424`, `8.0.426`, `9.0.306`
- Visual Studio was stopped and restarted.
- Hot Reload was disabled temporarily. A separate Edit and Continue switch was not found.
- Visual Studio update and SDK update were not applied during this acceptance run.

Supporting commands: `git status --short`, `git branch --show-current`, `git rev-parse HEAD`, `git diff --stat`, `git diff --check`, `dotnet sln ".\\FLASH ROUTER.sln" list`, `dotnet --version`, `dotnet --list-sdks`, and `Get-NetTCPConnection` for ports 5270/5271.
