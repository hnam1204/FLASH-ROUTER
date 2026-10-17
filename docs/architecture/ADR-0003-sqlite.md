# ADR-0003: SQLite technical boundary

Status: Implemented for BE-01

Foundation owns the technical SQLite boundary: local path resolution, connection-string construction, WAL/foreign-key/busy-timeout setup, retry handling, the shared `FlashAiDbContext`, and the single host-owned migration coordinator. Module entities and mappings enter the model through `IFlashAiModelContributor`; Foundation does not reference module-private types.

The Integrated database is `.local/data/flashrouter.db`. Each operation creates a short-lived context through `IDbContextFactory<FlashAiDbContext>`. Preview mode remains an explicit in-memory composition and does not touch SQLite; Integrated mode fails closed if migration or persistence fails.

The host owns one migration pipeline in `FLASH ROUTER`:

- `InitialAccountPersistence`
- `AddProviderConnectionAccountForeignKey`
- `AddTrackingDateTicksUtc`

The schema contains `AccountProfiles`, `AccountTags`, `ProviderConnections`, and `__EFMigrationsHistory`. No migration seeds preview accounts. Account/profile uniqueness is `(NormalizedEmail, NormalizedProvider)`; provider connections are metadata-only and contain no credential or token fields.

Existing databases must be inspected and backed up before future destructive schema changes. BE-01 only applied the pending non-destructive migrations to the newly created workspace-local database; no database file was recreated and no migration history was removed.
