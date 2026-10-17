# Connections module

## BE-03 through BE-05 boundary

This module owns the `/connections` route, its isolated UI stylesheet, public registration entry point, official OAuth contracts, provider identity, connection-session metadata, provider capability adapters, credential references, check runs, normalized observation history, sync state, and truthful status projections. The implementation is offline-first. BE-03 OAuth and BE-04/05 provider verification are offline implemented/tested; live authorization and live provider verification remain `NOT VERIFIED` until eligibility, registration, user consent, explicit credentials, and explicit live-test approval are available.

## Planned ownership

- UI: UI/ConnectionsPage.razor and its paired .razor.css file.
- Contracts: public ports and read models for other modules.
- Application: registration/start, callback validation, refresh, disconnect, provider capability resolution, authorized checks, Check All orchestration, and UI status boundaries.
- Domain: registration, session, vault-reference, provider capability, check, observation, and sync records.
- Persistence: additive EF mappings for OAuth and provider integration metadata in the existing SQLite database; plaintext credentials and raw responses are excluded.
- Tests: synthetic OAuth/provider fixtures only; no live provider endpoint is called.

## Dependencies

This module references FlashAI.Foundation only. Cross-feature behavior must use versioned public contracts or projections; private persistence namespaces are not a public API.

