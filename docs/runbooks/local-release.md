# Local release runbook

BE-06 publishes a self-contained Windows `win-x64` artifact with the available SDK/runtime toolchain. The solution remains `net8.0` because this machine provides SDK 8/9 and not .NET 10.

```powershell
dotnet publish '.\FLASH ROUTER\FLASH ROUTER.csproj' -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o '.local\releases\BE-06\win-x64'
```

The certified local artifact from BE-06 is `.local/releases/BE-06/win-x64-verified-3`. It contains the application and static assets only; it must not contain `.local` runtime data, SQLite databases, DPAPI key rings, vault ciphertext, logs, credentials, or user data.

Cold-start verification uses an isolated `FlashAI__WorkspaceRoot`, `FlashAI__AccountStorageMode=Integrated`, and a loopback-only `ASPNETCORE_URLS` value. Verify migration startup, `/`, `/connections`, `/backups`, a representative static asset, listener address, restart, and graceful shutdown before distributing the artifact. Live OAuth/provider verification is not part of a local release gate.

## BE-07 current-source artifact

BE-07 publishes the current source to `.local/releases/BE-07/win-x64-verified-3`. Its manifest and 387 checksums are under `.local/releases/BE-07/`. The artifact remains `net8.0`, self-contained `win-x64`, unsigned, localhost-only, and offline/provider-unverified. Do not replace the active database or DPAPI key ring while testing it; use an isolated `FlashAI__WorkspaceRoot` under `.local/test-data`.

BE-07 also verifies that a failed provider check does not hide the latest non-empty observation snapshot. This is an offline data-integrity regression, not live provider certification.

## CLI-01 Windows package

Publish the host and launcher separately so the final package can be assembled without overwriting a running executable:

```powershell
dotnet publish '.\FLASH ROUTER\FLASH ROUTER.csproj' -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o '.\.local\releases\CLI-01\win-x64\host-publish'
dotnet publish '.\src\FlashAI.Cli\FlashAI.Cli.csproj' -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o '.\.local\releases\CLI-01\win-x64\launcher-publish'
```

Copy both outputs into a new versioned package directory, omit `appsettings.Development.json`, generate a SHA-256 manifest, and scan the final directory for `.local`, SQLite, Vault, key-ring, and runtime-secret files. The validated CLI-01 package is `.local/releases/CLI-01/win-x64/package-release-v4`.

The package is not installed automatically. Use `tools/Install-FlashRouter.ps1` with `-PathFile` and a workspace install root for sandbox testing. Use `-ApplyUserPath` only after explicit approval; it changes only the current user's PATH and never Machine PATH. Do not use `setx`.
