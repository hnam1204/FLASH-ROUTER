# SQLite Provider Schema

The application uses one database: `.local/data/flashrouter.db`. Provider integrations extend the host-owned EF migration pipeline.

| Table | Purpose | Secret policy |
| --- | --- | --- |
| `ProviderConnections` | Provider/account/connection kind/status metadata | No credentials |
| `ProviderCredentialReferences` | Provider-scoped vault key and credential metadata | No plaintext key |
| `ProviderCapabilityDefinitions` | Durable capability/endpoint metadata | No credentials |
| `ProviderCheckRuns` | Safe operation status, correlation, and timestamps | No raw response |
| `ProviderObservations` | Normalized metric values, units, currency, scope, freshness/status | No raw response or authorization header |
| `ProviderSyncStates` | Disabled-by-default schedule/backoff state | No credentials |

Provider observations retain scope and time bucket identity. Organization, project, workspace, key, model, and billing-account observations are not silently summed. Monetary values remain decimal strings with explicit currency; token and request units remain separate.

Migrations `20261015175059_AddProviderIntegrationPersistence` and `20261015175327_AddProviderIntegrationForeignKeys` are additive. The migration was applied to a disposable copy first, then the local database was backed up and migrated. SQLite integrity remained `ok`.
