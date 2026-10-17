# Data ownership

| Owner | Owns | Public boundary |
| --- | --- | --- |
| Accounts | Account records, tags, archive state | Account ID and account summary |
| Connections | Provider identity, authorized session metadata, provider credential references, capability checks, and normalized observations | Connection and provider observation snapshots |
| Subscriptions | User-owned subscription records and provenance | Subscription summary |
| Authenticator | Local TOTP credential metadata and encrypted vault references | No plaintext secret value |
| ImportExport | Parse and preview workflow | Import row results |
| Reminders | Reminder records and job state | Scheduler port |
| BackupRestore | Backup format, encrypted payload metadata, and disposable recovery workflow | Backup status |
| Activity | Safe audit events | Audit sink |
| Settings | Local preferences | Settings snapshot |
| Diagnostics | Redacted host/module health | Diagnostics snapshot |
| Dashboard | Composition of public read models | Dashboard contributions |

No module may query another module's private entity, repository, or table directly.

BE-01 SQLite ownership is composed in the host but remains module-owned at the model boundary:

- Accounts owns `AccountProfiles` and `AccountTags`, including validation, search/filter/sort, lifecycle state, tags, favorite state, and concurrency versioning.
- Connections owns metadata-only `ProviderConnections`, including the AI Web/API `ConnectionKind`; it does not store credentials, tokens, cookies, passwords, or TOTP seeds.
- `FlashAiDbContext`, the migration coordinator, and SQLite connection policy are technical composition infrastructure, not business ownership.

BE-02 extends Authenticator ownership with encrypted vault references and local TOTP credential envelopes. BE-03 extends Connections ownership with OAuth registrations, authorization attempts, provider identity, connection sessions, token-vault references, and verification observations. BE-04/05 extend that ownership with provider capability definitions, check runs, normalized observation history, sync state, and provider-scoped credential references. Plaintext password, TOTP, API-key, access-token, refresh-token, or ID-token values do not belong in `AccountProfiles`, `ProviderConnections`, OAuth metadata tables, provider observation tables, ordinary ViewModels, diagnostics, or logs. The Foundation `ISecretVault` is the technical storage boundary; modules supply ownership-scoped logical keys and metadata. Provider-reported organization, project, workspace, key, model, and billing-account scopes remain distinct and are never silently summed.

BE-06 stores only backup metadata and encrypted bytes under `.local/backups`. It may read the host-owned SQLite database through the connection factory and may copy already-encrypted vault ciphertext, but it never exports the DPAPI key ring, plaintext credentials, or active database replacement state.

UI-E adds the following ownership rules:

- Accounts owns the account identity and display metadata used to select a browser profile.
- Connections owns `BrowserProfileBindings`, including provider, browser kind, opaque profile identifier, managed path reference, launch state, and timestamps.
- Connections does not own Chrome cookies, local storage, passwords, profile tokens, or session exports.
- Account copy operations may return metadata by default. Credential copy is a Vault-gated, confirmation-gated transient operation and is not persisted to SQLite or logs.
