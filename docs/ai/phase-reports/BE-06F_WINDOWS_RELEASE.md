# BE-06F — Windows Release

**Ngày:** 2026-10-16  
**Kết luận:** `BE-06F WINDOWS RELEASE — GO (artifact certification; conditional security status)`

## Environment

- Current SDK: `9.0.306`; installed SDK lines include `8.0.424`, `8.0.426` and `9.0.306`.
- Solution target: `net8.0`; release runtime: `win-x64`; publish mode: self-contained.
- No .NET 10 SDK is installed. .NET 8 is approaching the end of its support window, so framework upgrade is a separate planned risk item.
- The artifact is unsigned; no code-signing claim is made.

## Publish and package evidence

Publish command:

```powershell
dotnet publish '.\FLASH ROUTER\FLASH ROUTER.csproj' -c Release -r win-x64 --self-contained true --no-restore -p:UseAppHost=true -o '.\.local\releases\BE-06\win-x64-verified-3'
```

Artifact: `.local/releases/BE-06/win-x64-verified-3`  
Inventory: 388 files, 111,593,635 bytes  
Entry assembly SHA-256: `6D4357780838C55611C1E152939AD684D6F0BD703B13AF455AC4626A63B3CEC6`

The package contains no SQLite database, vault ciphertext, DPAPI key ring, local runtime data, logs, test fixtures or real credentials. `manifest.json` and `checksums.sha256` are generated at `.local/releases/BE-06/`.

## Cold-start evidence

- Migrations applied successfully in an isolated workspace.
- `/`, `/connections`, `/backups` and `/app.css` returned `200`.
- Listener was `127.0.0.1:5281`; restart and graceful shutdown passed.
- Production release was used; no Debug-only launch claim is used.

## Gate

`GO` for the reproducible local Windows artifact. This does not override the BE-06C dependency advisory warning or the unverified live provider/OAuth status.
