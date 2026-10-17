# CLI-01 Windows Command Launcher

Date: 2026-10-16

## Scope and actual state

CLI-01 adds a real Windows `flashrouter` launcher around the existing ASP.NET Core host. The host remains the existing `FLASH ROUTER` project; no second backend or Windows service was introduced. The solution target remains `net8.0`.

The workspace has no Git metadata (`git status` is unavailable), so the change inventory below is the source-of-truth inventory rather than a Git diff.

## Change inventory

- Added `src/FlashAI.Cli`, a small launcher project with apphost publishing and `flashrouter.cmd`.
- Added authenticated managed-host control through `src/FlashAI.Foundation/Hosting/ManagedHostControl.cs`.
- Added runtime/control paths to `src/FlashAI.Foundation/Persistence/LocalDataPaths.cs`.
- Added `/health/ready` to the existing host in `FLASH ROUTER/Program.cs`.
- Added `tools/Install-FlashRouter.ps1` for staged per-user installation preparation and PATH-file sandboxing.
- Added focused CLI tests in `tests/FlashAI.IntegrationTests/CliLauncherTests.cs`.
- Added this report and `docs/runbooks/windows-cli.md`; existing development and release runbooks were extended below.

## Command architecture

The launcher resolves its installation directory, stable data root, loopback endpoint, host executable, and browser without depending on the current directory. Supported commands are:

| Command | Behavior |
| --- | --- |
| `flashrouter` | Start if needed, wait for readiness, then open the browser. |
| `open` | Reuse or start the managed host, then open the browser. |
| `status` | Show verified managed PID, version, endpoint, and readiness. |
| `stop` | Send authenticated graceful shutdown to the managed host only. |
| `restart` | Gracefully stop, then start and open the new host. |
| `logs` | Show the redacted launcher log tail. |
| `version` | Show launcher version and selected host. |
| `help` | Show usage. |

Single-instance coordination uses a per-data-root named semaphore plus an authenticated, current-user-only named pipe. The launcher verifies the response against runtime instance metadata and the live process before treating the host as managed. It never uses `taskkill`, a PID alone, or a port check as ownership proof.

## Local startup and browser behavior

The host binds to the configured loopback endpoint, with the CLI defaulting to `http://127.0.0.1:5270`. Production HTTP-only CLI launches do not apply an HTTPS redirect when no HTTPS endpoint is configured. Readiness is reported only after host startup and integrated migration initialization complete.

Chrome is preferred when `FLASHROUTER_BROWSER_PATH` or a standard Chrome path resolves. Otherwise the launcher asks Windows for the default browser. No credentials, cookies, remote debugging flags, or Chrome profile data are passed to the browser. `FLASHROUTER_NO_BROWSER=1` is a test-only switch used for isolated certification.

## Data preservation and security

The launcher passes `FlashAI__WorkspaceRoot` to the existing host. The host therefore keeps the selected `.local/data/flashrouter.db`, `.local/security`, `.local/backups`, and Data Protection key ring instead of creating a second database beside the release binaries. Disposable E2E data was used; the existing user data root was not reset, deleted, or migrated.

Control secrets are short-lived, stored BOM-free in `.local/runtime/control.secret`, and never included in status or launcher logs. The control pipe is restricted to the current Windows user and still requires the secret. Runtime markers are deleted on graceful stop.

## Release packaging

The current self-contained package was published with:

```powershell
dotnet publish '.\FLASH ROUTER\FLASH ROUTER.csproj' -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o '.\.local\releases\CLI-01\win-x64\host-publish'
dotnet publish '.\src\FlashAI.Cli\FlashAI.Cli.csproj' -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o '.\.local\releases\CLI-01\win-x64\launcher-publish'
```

The validated combined package is `.local/releases/CLI-01/win-x64/package-release-v4`. It contains `FLASH_ROUTER.exe`, `flashrouter.exe`, `flashrouter.cmd`, static assets, and `e_sqlite3.dll`. It excludes `appsettings.Development.json`, `.local` data, SQLite files, Vault files, runtime secrets, and key-ring data. `manifest.json` contains SHA-256 checksums for 392 package files.

The failed attempt to copy a new package over a running release reproduced the Windows executable/DLL lock behavior. The release workflow now uses versioned staging directories and never overwrites a running package.

## Evidence

- Debug solution build with apphost disabled: 0 errors; existing warnings only.
- Architecture tests: `8/8` passed.
- Integration tests: `108/108` passed, including CLI parser, scoped control names, workspace-root resolution, and no-secret response serialization.
- Installer sandbox: install, idempotent reinstall, exact PATH-file update, command-collision refusal, and uninstall all passed. No User/Machine PATH was changed.
- Release E2E from `C:\Windows\System32`: start, second-start reuse, status, open, logs, version, restart, stop, and stopped-state checks passed.
- E2E verified one reused PID, a changed PID after restart, no listener after stop, persistent SQLite data, preserved security directory/key file, and zero runtime marker files after shutdown.
- Browser auto-open code was exercised with `FLASHROUTER_NO_BROWSER=1` to avoid touching the user's existing browser sessions. A real browser-window acceptance run remains unexecuted.

## Verdicts

- CLI-01A architecture: `GO`
- CLI-01B commands: `GO`
- CLI-01C local startup/readiness: `GO`; real browser-window run remains a test gap
- CLI-01D Windows publish: `GO`
- CLI-01E installer sandbox: `GO`
- CLI-01E real user install: `NOT AUTHORIZED`
- CLI-01F E2E: `PARTIAL` because browser-window auto-open was intentionally not run
- Overall CLI-01: `PARTIAL`

The implementation is ready for an explicitly approved per-user installation and a controlled browser acceptance run. No live installation, User PATH mutation, Machine PATH mutation, OAuth call, provider call, or user-data migration was performed.
