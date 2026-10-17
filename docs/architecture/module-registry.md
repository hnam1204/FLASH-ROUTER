# Module registry

| Key | Route | Registration | Phase status |
| --- | --- | --- | --- |
| Dashboard | `/dashboard` | `AddDashboardModule` | SCAFFOLDED |
| Accounts | `/accounts` | `AddAccountsModule` | BE-01 INTEGRATED; UI-E account connection UX OFFLINE GO |
| Connections | `/connections` | `AddConnectionsModule` | BE-03/BE-03L OFFLINE GO; BE-04/05 OFFLINE GO; live OAuth/provider usage NOT VERIFIED |
| Subscriptions | `/subscriptions` | `AddSubscriptionsModule` | SCAFFOLDED |
| Authenticator | `/authenticator` | `AddAuthenticatorModule` | BE-02 INTEGRATED TOTP/VAULT; Preview remains explicit |
| ImportExport | `/import-export` | `AddImportExportModule` | SCAFFOLDED |
| Reminders | `/reminders` | `AddRemindersModule` | SCAFFOLDED |
| BackupRestore | `/backups` | `AddBackupRestoreModule` | BE-06 LOCAL GO; profile-bound encrypted backup and disposable restore validation |
| Activity | `/activity` | `AddActivityModule` | SCAFFOLDED |
| Settings | `/settings` | `AddSettingsModule` | SCAFFOLDED |
| Diagnostics | `/diagnostics` | `AddDiagnosticsModule` | SCAFFOLDED |

BE-01 adds one host-owned SQLite migration pipeline for Accounts and metadata-only Connections persistence. BE-03 adds offline official OAuth contracts and additive SQLite metadata tables while preserving live eligibility and browser authorization as explicit gates. BE-04 adds capability-based adapters for OpenRouter, OpenAI Platform, Anthropic, Gemini, DeepSeek, and Groq; BE-05 adds authorized manual checks, explicit-confirmation Check All, transactional observation history, and a disabled-by-default scheduler. Provider credentials remain in the BE-02 vault, while SQLite stores references, status, and normalized observations only.

BE-06 integrates `BackupRestore` through the host registration. Its active implementation owns backup manifests, encrypted SQLite payloads, encrypted vault ciphertext snapshots, path/reparse validation, checksum verification, and disposable restore validation. It does not own the application database schema or DPAPI key ring.

UI-E adds account-scoped credential import, secure copy gates, and Chrome profile bindings through the Accounts and Connections boundaries. Browser bindings store only provider, browser, opaque profile identifier, managed directory reference, state, and timestamps. Chrome launch is a user handoff and never an authentication or usage assertion.
