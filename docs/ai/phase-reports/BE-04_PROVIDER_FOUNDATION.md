# BE-04A Provider Foundation

Date: 2026-10-16  
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Executive summary

BE-04A adds a capability-based provider foundation inside the existing Connections module. It supports independent adapters for OpenRouter, OpenAI Platform, Anthropic, Gemini, DeepSeek, and Groq without creating a second host or database. Provider credentials are written only to the BE-02 vault; SQLite receives references and observations, never raw keys.

Verdict: `BE-04A - GO (OFFLINE)`.

## Implementation

- Contracts: `src/Modules/Connections/Features/ProviderIntegration/Abstractions/ProviderContracts.cs`.
- Registry/capabilities: `Registry/ProviderRegistry.cs`.
- Credential resolution: `CredentialResolution/ProviderCredentialVaultStore.cs`.
- Fixed-host HTTP: `Verification/ProviderHttpExecutor.cs`.
- Error mapping and orchestration: `Verification/ProviderErrorMapper.cs`, `ProviderConnectionVerifier.cs`, and `ProviderSyncOrchestrator.cs`.
- SQLite catalog/history: `src/Modules/Connections/Persistence/ProviderConnectionCatalog.cs` and `ProviderObservationStores.cs`.
- Adapters: `Providers/OpenRouter`, `OpenAIPlatform`, `Anthropic`, `Gemini`, `DeepSeek`, and `Groq`.

## Security boundary

- Each credential is scoped by provider, connection, credential ID, and credential kind.
- API keys are stored through `ISecretVault` at provider/connection-specific logical keys.
- SQLite stores only vault references, connection metadata, check runs, and normalized observations.
- HTTP clients use fixed official HTTPS base hosts, response size limits, cancellation, timeout, retry classification, and disabled redirects.
- Standard and administrative credentials are distinct; organization/project reports are never inferred from a normal key.
- Unsupported permissions are reported as `Unsupported`, `Forbidden`, or `InsufficientPermission`; missing values are not replaced with zero.

## SQLite changes

Additive migrations:

- `20261015175059_AddProviderIntegrationPersistence`.
- `20261015175327_AddProviderIntegrationForeignKeys`.

Tables: `ProviderCredentialReferences`, `ProviderCapabilityDefinitions`, `ProviderCheckRuns`, `ProviderObservations`, and `ProviderSyncStates`. Foreign keys point to the existing `ProviderConnections` and check-run records. A disposable-copy migration test passed before the existing database was migrated.

## Verification

- Architecture tests: `8/8` passed.
- Full integration tests after foundation/UI integration: `70/70` passed.
- Focused provider tests: `8/8` initially, then `8/8` after fixed-host and credential-binding hardening.
- No live provider calls, real API keys, or billable requests were used.
- Existing database was backed up to `.local/test-data/be04-db-preflight-20261016.db` before migration.
- Integrated host applied both additive migrations successfully; `/connections` returned HTTP 200 and rendered the provider adapter panel.

## Live status

`OFFLINE IMPLEMENTED` and `OFFLINE TESTED`. Live provider verification is `NOT VERIFIED`; it requires explicit credentials and approval and is intentionally not part of this phase.

## Remaining limits

- Provider reports are read-only and fixture-verified; live provider response drift must be checked before production use.
- Capability definitions are registry-backed; the SQLite definition table is reserved for durable metadata and auditability.
- API key entry UI composition remains intentionally minimal; raw credentials never appear in rendered output or SQLite.
