# Backup / Restore module

## BE-06 recovery boundary

This module owns the `/backups` route, its isolated UI stylesheet, public registration entry point, and local recovery contracts. Integrated mode creates encrypted local SQLite snapshots using the SQLite Online Backup API, records safe manifest metadata, copies only already-encrypted vault ciphertext, and validates restore into disposable test storage. It never overwrites the active database or DPAPI key ring.

## Planned ownership

- UI: UI/BackupRestorePage.razor and its paired .razor.css file.
- Contracts: public ports and read models for other modules.
- Application: create/list backup snapshots, checksum and encryption validation, disposable restore rehearsal, and safe error mapping.
- Domain: backup manifest metadata and integrity/compatibility state.
- Persistence: local `.local/backups` artifacts; the one application SQLite database remains owned by the host migration pipeline.
- Tests: synthetic disposable databases only; no active user database is overwritten and no portable backup claim is made.

## Dependencies

This module references FlashAI.Foundation only. Cross-feature behavior must use versioned public contracts or projections; private persistence namespaces are not a public API.

