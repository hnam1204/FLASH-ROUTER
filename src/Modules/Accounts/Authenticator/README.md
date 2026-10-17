# Authenticator module

## BE-02 boundary

This module owns the /authenticator route, its isolated UI stylesheet, public registration entry point, RFC 6238 generation, and the local credential-vault application boundary. Integrated mode uses the Foundation `ISecretVault`; Preview mode remains explicitly synthetic and in-memory.

## Planned ownership

- UI: UI/AuthenticatorPage.razor and its paired .razor.css file.
- Contracts: public ports and read models for other modules.
- Application: DPAPI-backed vault session, TOTP commands/queries, and a memory-only legacy credential import session.
- Domain: feature-owned records and state transitions.
- Persistence: encrypted vault values and masked metadata index; no plaintext credential columns or files.
- Tests: synthetic vault round-trip, wrong key-ring failure, RFC 6238 vectors, lock/persistence, masking, and import validation.

## Dependencies

This module references FlashAI.Foundation only. Cross-feature behavior must use versioned public contracts or projections; private persistence namespaces are not a public API.

