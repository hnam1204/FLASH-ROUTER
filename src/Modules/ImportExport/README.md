# Import / Export module

## Phase 0A boundary

This module owns the /import-export route, its isolated UI stylesheet, public registration entry point, and future feature contracts. The route is a truthful scaffold and does not simulate provider, credential, plan, quota, or persistence results.

## Planned ownership

- UI: UI/ImportExportPage.razor and its paired .razor.css file.
- Contracts: public ports and read models for other modules.
- Application: commands and queries for a later phase.
- Domain: feature-owned records and state transitions.
- Persistence: feature-owned EF mappings and repositories after Phase 0B.
- Tests: module behavior and boundary tests before the feature is enabled.

## Dependencies

This module references FlashAI.Foundation only. Cross-feature behavior must use versioned public contracts or projections; private persistence namespaces are not a public API.

