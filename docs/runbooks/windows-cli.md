# Windows CLI Runbook

## Test invocation

The CLI is published as a self-contained `win-x64` apphost. For a disposable run from an arbitrary directory:

```powershell
$env:FLASHROUTER_DATA_ROOT = 'D:\path\to\disposable-root'
$env:FLASHROUTER_ENDPOINT = 'http://127.0.0.1:5288'
$env:FLASHROUTER_HOST_PATH = 'D:\path\to\package\FLASH_ROUTER.exe'
$env:FLASHROUTER_NO_BROWSER = '1'
& 'D:\path\to\package\flashrouter.exe'
```

The supported commands are `flashrouter`, `open`, `status`, `stop`, `restart`, `logs`, `version`, and `help`. The default command starts the managed host only when needed, waits for `/health/ready`, and then opens the configured browser. `status` and `stop` use the authenticated per-user named pipe; they do not act on a port or executable name alone.

## Data and control files

The host uses the configured root for `.local/data`, `.local/security`, `.local/backups`, `.local/logs`, and `.local/runtime`. The runtime directory contains only short-lived control metadata and is cleaned after graceful shutdown. SQLite, Vault, and the existing Data Protection key ring are not moved by CLI-01.

## Workspace installer sandbox

Do not run a live installation during development. Use a PATH file and an installation directory inside the workspace:

```powershell
.\tools\Install-FlashRouter.ps1 `
  -Action Install `
  -ReleaseRoot '.\.local\releases\CLI-01\win-x64\package-release-v4' `
  -InstallRoot '.\.local\test-data\cli01-installer\Programs\FlashRouter' `
  -PathFile '.\.local\test-data\cli01-installer\user-path.txt'
```

The script stages the copy, writes an installation marker, refuses unmarked destination collisions, prevents duplicate PATH entries, checks command-name collisions, and keeps a prior marked release as a rollback backup. `-Action Uninstall` removes only the marked installation and its exact PATH entry.

`-ApplyUserPath` is the explicit switch that permits changing the current user's PATH. It must not be used without separate user approval. The script never changes Machine PATH and never uses `setx`.

## Browser runtime validation

AUTH-BROWSER-02 validation uses the Visual Studio http and https launch-profile settings through dotnet run with a disposable FlashAI__WorkspaceRoot. It verifies the live host and static assets before shutdown; a build pass alone is not treated as runtime evidence. No live browser login or real account is part of this command-line validation.

## Release updates

Never publish or copy over a running host directory. Publish each candidate to a new staging/versioned directory, validate it, gracefully stop the exact managed instance through `flashrouter stop`, and only then switch the active package or install it. Keep the previous marked release for rollback.
