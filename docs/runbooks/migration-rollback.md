# Migration rollback runbook

BE-01 uses one host-owned EF Core migration pipeline. Integrated startup applies pending migrations to `.local/data/flashrouter.db`; Preview mode does not open SQLite.

Before changing an existing database:

1. Stop the application and create a consistent SQLite backup, including WAL state as appropriate.
2. Run `PRAGMA integrity_check` and record the current `__EFMigrationsHistory`.
3. Review the generated migration for destructive operations. Do not apply a drop, truncate, database recreation, or migration-history rewrite without explicit approval.
4. Apply the migration only to the intended local database and verify tables, indexes, foreign keys, integrity, and row counts.

SQLite migrations do not provide a general automatic down-migration guarantee. Recovery is therefore backup restore into a separate test path, followed by integrity and application smoke checks. Never use `EnsureDeleted()` against the user database; the integration test fixture uses unique `.local/test-data/be01-*` paths only.

Provider observation reads retain the newest non-empty snapshot after failed, rate-limited, or unavailable checks. Failed check runs remain in history for diagnostics. SQLite provider run ordering is materialized before UTC-aware ordering because the SQLite provider does not translate `DateTimeOffset` ordering directly.

BE-06 operational sequence:

1. Stop the host and record the current migration id, row-count summary, and `PRAGMA integrity_check` result.
2. Create a full encrypted local backup and confirm that its manifest reports `IntegrityStatus=ok`.
3. Apply the additive migration and rerun integrity, foreign-key, schema, and route smoke checks.
4. If recovery is needed, validate the backup into disposable `.local/test-data/be06-restore-*` storage. Do not replace the active database automatically; perform replacement only through a separately approved, documented maintenance procedure.

For a future .NET 10 migration, repeat the same sequence on a disposable copy and verify vault decryption with the existing DPAPI key ring before any production-profile start. Do not regenerate keys or rewrite migration history.
