# Accounts module

## BE-01 boundary

This module owns the Accounts contracts, public registration entry point, and persistence boundary. The former `/accounts` Account Manager UI was removed in CONSOLE-03; the module is intentionally backend-only until a future product surface is explicitly approved. Integrated mode uses the host-owned SQLite database through typed ports; Preview mode remains an explicit in-memory composition.

## Planned ownership

- Contracts: public ports and read models for other modules.
- Application: validation, SQLite commands, and SQLite browse/detail queries.
- Domain: feature-owned records and state transitions.
- Persistence: `AccountModelContributor`, `AccountProjectionMapper`, `AccountProfiles`, and `AccountTags`.
- Tests: SQLite integration coverage for CRUD, search/filter/sort, lifecycle, constraints, and concurrency.

BE-01 does not store provider credentials, OAuth tokens, cookies, passwords, API keys, or TOTP seeds. Delete is an explicit user-confirmed action in the existing UI; archive remains the non-destructive lifecycle action.

## Dependencies

This module references FlashAI.Foundation only. Cross-feature behavior must use versioned public contracts or projections; private persistence namespaces are not a public API.

