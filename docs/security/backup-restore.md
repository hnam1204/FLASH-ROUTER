# Backup and restore

BE-06 enables local encrypted recovery in Integrated mode. The active SQLite database is copied with SQLite Online Backup into temporary storage, inspected with `PRAGMA integrity_check`, encrypted with the existing Windows-user Data Protection key ring, checksummed with SHA-256, and written through an atomic replace. The backup also copies only already-encrypted `.secret` vault ciphertext; the DPAPI key ring is never included.

Restore is deliberately a disposable validation operation. It verifies manifest version, payload checksum, DPAPI unprotectability, SQLite integrity/schema/count metadata, vault file count, and credential-reference coverage, then deletes the temporary restored database. It never overwrites the active database or key ring and requires explicit UI confirmation.

Operational limits: backups are profile-bound (`DataProtection-DPAPI`), portable cross-user restore is unsupported, live credentials/providers are not contacted, and a missing/corrupt backup must not permanently block later backup operations.
