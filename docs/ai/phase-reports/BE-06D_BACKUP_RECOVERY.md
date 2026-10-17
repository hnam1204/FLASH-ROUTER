# BE-06D — SQLite Backup, Restore & Recovery

**Ngày:** 2026-10-16  
**Kết luận:** `BE-06D RECOVERY — GO (local/profile-bound scope)`

## Implemented recovery contract

- SQLite snapshots use the SQLite Online Backup API rather than copying an active WAL database directly.
- The snapshot is checked with `PRAGMA integrity_check`; schema version, source database identifier, file size, checksum, encryption status and safe counts are recorded in the manifest.
- Backup payloads are protected with the existing Windows-user Data Protection key ring and written atomically.
- Only already-encrypted vault ciphertext is copied. The DPAPI key ring, plaintext secrets and active database replacement state are excluded.
- Restore validates manifest version, SHA-256 payload checksum, DPAPI unprotectability, SQLite integrity/schema/count metadata, vault file count and credential-reference coverage in a disposable workspace.
- Restore never overwrites the active database and removes its temporary restored database after validation. Path traversal and reparse-point checks are enforced.

## Evidence

- `Be06BackupRecoveryTests`: `3/3 PASS` inside the full `73/73` integration suite.
- Covered cases: encrypted backup plus disposable restore, corrupted payload rejection without source mutation, and backup-name traversal rejection.
- Final release `/backups` route returned HTTP `200`.

## Limitations

Recovery is profile-bound to the Windows-user Data Protection key ring. A backup is not a portable cross-user migration package. Disk-full, permissions and missing-key-ring recovery remain operational scenarios requiring the local runbooks; no active user database or key ring was overwritten during certification.

## Gate

`GO` for local encrypted backup, integrity validation and disposable restore validation.
