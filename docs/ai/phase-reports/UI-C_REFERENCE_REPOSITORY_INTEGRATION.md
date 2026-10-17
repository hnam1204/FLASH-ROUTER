# UI-C-R / BE-03-R - Reference Repository Integration

Date: 2026-10-16  
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Verdict at a glance

`OFFLINE GO` for the reference analysis and architecture mapping.

`LIVE NO-GO` for official ChatGPT OAuth and every provider capability that is not backed by an approved official API/authorization contract.

This addendum studies the two user-provided repositories. It does not copy source, assets, OAuth clients, credentials, private endpoints, browser cookies, or provider sessions. No production source, SQLite migration, Vault file, `.local/security` file, or UI architecture was changed by this report-only addendum.

## 1. Repository names

1. [`Lampese/codex-switcher`](https://github.com/Lampese/codex-switcher)
2. [`jlcodes99/cockpit-tools`](https://github.com/jlcodes99/cockpit-tools)

The repositories were used as design references only. Existing FLASH ROUTER source and official OpenAI documentation remain the source of truth.

## 2. Branch and commit SHA

The audit is pinned to these revisions so the evidence is reproducible:

| Repository | Branch | Audited commit |
|---|---|---|
| `Lampese/codex-switcher` | `main` | `c357686d4bdf306da272da53c9831b12b121526a` |
| `jlcodes99/cockpit-tools` | `main` | `5e7b3c1e383e16f3ef2f0af2432d93d3944c7e16` |

The upstream `cockpit-tools` `main` branch has moved since this audit; this report intentionally refers to the pinned commit above, not to an unpinned moving branch.

## 3. Source files read

### `codex-switcher`

- `README.md`
- `src-tauri/src/auth/oauth_server.rs`
- `src-tauri/src/auth/storage.rs`
- `src-tauri/src/auth/switcher.rs`
- `src-tauri/src/auth/token_refresh.rs`
- `src-tauri/src/api/usage.rs`
- `src-tauri/src/commands/account_stats.rs`
- `src/components/AddAccountModal.tsx`
- `src/components/AccountCard.tsx`
- `src/components/AccountUsageStats.tsx`
- `src/components/UsageBar.tsx`
- `src/hooks/useAccounts.ts`

### `cockpit-tools`

- `README.md`
- `README.en.md`
- `SECURITY.md`
- `crates/cockpit-core/Cargo.toml`
- `crates/cockpit-core/src/modules/codex_oauth.rs`
- `crates/cockpit-core/src/modules/codex_quota.rs`
- `crates/cockpit-core/src/modules/oauth_pending_state.rs`
- `crates/cockpit-core/src/modules/quota_cache.rs`
- `crates/cockpit-core/src/modules/atomic_write.rs`
- `crates/cockpit-core/src/modules/github_copilot_account.rs`
- `crates/cockpit-core/src/modules/github_copilot_oauth.rs`
- `crates/cockpit-core/src/modules/cursor_account.rs`
- `crates/cockpit-core/src/modules/kiro_account.rs`
- `crates/cockpit-core/src/modules/windsurf_account.rs`

### Official OpenAI documentation checked

- [Sign in with ChatGPT quickstart](https://developers.openai.com/siwc/quickstart.md)
- [Request a client ID](https://developers.openai.com/siwc/request-client-id.md)
- [Open-source sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in.md)
- [Profiles and sessions](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions.md)
- [Token reference](https://developers.openai.com/siwc/token-sharing-open-source/token-reference.md)
- [Errors and recovery](https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery.md)
- [Preview limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations.md)

## 4. License findings

- `codex-switcher`: no top-level `LICENSE` file or recognized GitHub license was available at the audited revision. Public visibility is not permission to reuse code, assets, translations, icons, or fixtures.
- `cockpit-tools`: `crates/cockpit-core/Cargo.toml` declares `CC-BY-NC-SA-4.0`; the README also describes the project as CC BY-NC-SA 4.0. This is not a blanket permission for FLASH ROUTER commercial integration or source reuse.
- No source, asset, icon, translation, test fixture, Rust dependency, TypeScript dependency, or license text was copied into FLASH ROUTER.

## 5. Features actually verified

The table separates source-verified behavior from README claims and records the security boundary for each idea.

| Repository / source area | Source-verified behavior | README or product claim | Security implication | FLASH ROUTER decision |
|---|---|---|---|---|
| `codex-switcher` account storage and account hooks | Multiple local account records can be selected and presented independently. | Multi-account management and quick switching. | Account selection and credential material are coupled to a local CLI auth-file workflow. | `ADOPT` the identity/picker concept; `REJECT` auth-file mutation. |
| `codex-switcher` OAuth server and storage | OAuth attempts keep local callback state and persist account/session material. | ChatGPT OAuth and `auth.json` import. | Uses an application-specific client and local plaintext JSON credential workflow. | `ADAPT` lifecycle ideas to official SIWC; never import `auth.json` or its client. |
| `codex-switcher` token refresh and switcher | Refresh/switch operations are coordinated to reduce late writes after token rotation. | Safe account switching while preserving rotated sessions. | Coordination is specific to its process and file format. | `ADOPT` per-connection serialization; implement independently with Vault and SQLite. |
| `codex-switcher` usage API and account stats | Quota windows, reset times, usage cards, daily buckets, and account statistics are modeled in UI/backend code. | Five-hour/weekly usage, credits, subscription expiry, and activity insights. | The usage path uses a private ChatGPT backend endpoint and browser-like request behavior. | `ADAPT` presentation and window concepts only; `REJECT` transport and unverified metrics. |
| `codex-switcher` warm-up paths | The app can issue scheduled or manual warm-up requests. | Warm up accounts around reset windows. | Can create billable/usage activity or be used to manipulate quota windows. | `REJECT`. |
| `cockpit-tools` provider modules | Provider-specific modules separate account, OAuth, quota, and credential logic. | One dashboard for many AI IDE providers. | Provider listing is not proof of official permission or stable API support. | `ADOPT` provider isolation; `RESEARCH` each provider independently. |
| `cockpit-tools` pending OAuth state | Pending authorization data is validated and invalid state is rejected/quarantined. | Provider OAuth account support. | The provider contracts and embedded clients are provider-specific. | `ADAPT` validation concepts to official OAuth contracts. |
| `cockpit-tools` quota cache | Cache entries preserve source/provider context and distinguish stale data from fresh data. | Quota monitoring and refresh. | Cached values can become misleading if treated as current or as zero. | `ADOPT` provenance and stale states through existing observations. |
| `cockpit-tools` atomic write | Local file writes use a temporary/replace pattern. | Safer local persistence. | Atomic file replacement does not make a Vault write and SQLite transaction atomic together. | `ADAPT` through existing Vault compensation and SQLite transactions. |
| `cockpit-tools` Copilot/Cursor/Kiro/Windsurf modules | Provider-specific credential/account models and UI paths exist in the reference. | Support for several AI IDEs and their quotas. | Some flows rely on client-local credentials, injection, or undocumented/private endpoints. | `RESEARCH` official contracts; `REJECT` credential injection and private endpoints. |

### Evidence register

The following register makes the source path, pinned revision, observed behavior, README claim, provider-support boundary, and adoption decision explicit. README statements are treated as claims, not as protocol evidence.

| Repository | Branch / commit | Source path | Feature | Behavior verified in source | Documentation claim | Official support status | Decision |
|---|---|---|---|---|---|---|---|
| `Lampese/codex-switcher` | `main` / `c357686d4bdf306da272da53c9831b12b121526a` | `src-tauri/src/auth/storage.rs`, `src/hooks/useAccounts.ts` | Multi-account identity | Local account records are enumerated, selected, and associated with account UI state. | README claims add, rename, mask, import, export, and manage multiple accounts. | Community behavior; not OpenAI authorization evidence. | `ADOPT` identity UX; `REJECT` file-based credential transport. |
| `Lampese/codex-switcher` | `main` / `c357686d4bdf306da272da53c9831b12b121526a` | `src-tauri/src/auth/oauth_server.rs`, `src/components/AddAccountModal.tsx` | OAuth attempt lifecycle | The app owns a local OAuth callback flow and account-add UI. | README claims ChatGPT OAuth and `auth.json` import. | Requires application-specific official registration; community client is not reusable. | `ADAPT` lifecycle; `REJECT` client/auth-file reuse. |
| `Lampese/codex-switcher` | `main` / `c357686d4bdf306da272da53c9831b12b121526a` | `src-tauri/src/auth/token_refresh.rs`, `src-tauri/src/auth/switcher.rs` | Refresh/switch synchronization | Refresh and switching are coordinated to reduce stale rotated-token writes. | README describes preserving the latest active session during switching. | Coordination is local to that application and file format. | `ADOPT` concept through per-connection .NET gates. |
| `Lampese/codex-switcher` | `main` / `c357686d4bdf306da272da53c9831b12b121526a` | `src-tauri/src/api/usage.rs`, `src-tauri/src/commands/account_stats.rs`, `src/components/AccountUsageStats.tsx` | Quota and usage | Usage cards model windows, reset times, daily buckets, and account statistics. | README claims five-hour/weekly limits, credits, expiry, and activity insights. | Uses private ChatGPT backend behavior; not an official FLASH ROUTER capability. | `ADAPT` data display; `REJECT` endpoint and inferred values. |
| `jlcodes99/cockpit-tools` | `main` / `5e7b3c1e383e16f3ef2f0af2432d93d3944c7e16` | `crates/cockpit-core/src/modules/oauth_pending_state.rs`, `codex_oauth.rs` | Provider OAuth isolation | Pending state and provider-specific OAuth modules are separated. | README claims OAuth account support across multiple platforms. | Each provider requires separate official validation and permission. | `ADAPT` pending-state pattern. |
| `jlcodes99/cockpit-tools` | `main` / `5e7b3c1e383e16f3ef2f0af2432d93d3944c7e16` | `crates/cockpit-core/src/modules/quota_cache.rs`, `codex_quota.rs` | Quota cache | Quota cache models source/provider data and stale refresh behavior. | README claims quota monitoring and reset-time views. | Cache semantics do not establish an official quota API. | `ADOPT` provenance/stale semantics. |
| `jlcodes99/cockpit-tools` | `main` / `5e7b3c1e383e16f3ef2f0af2432d93d3944c7e16` | `crates/cockpit-core/src/modules/atomic_write.rs` | Atomic persistence | Local state uses an atomic write helper. | README describes local account/config persistence. | File atomicity does not provide Vault/SQLite cross-store atomicity. | `ADAPT` with existing compensation. |
| `jlcodes99/cockpit-tools` | `main` / `5e7b3c1e383e16f3ef2f0af2432d93d3944c7e16` | `github_copilot_*`, `cursor_account.rs`, `kiro_account.rs`, `windsurf_account.rs` | Provider-specific accounts | Separate account modules model provider-specific credentials and quota paths. | README claims broad AI IDE support and account switching. | Listing or source behavior is not proof of official support for FLASH ROUTER. | `RESEARCH` official contracts; `REJECT` injection/private endpoints. |

## 6. Features that must not be copied

The following are explicitly outside the FLASH ROUTER trust boundary:

- Borrowed OAuth client IDs, including `app_EMoamEEZ73f0CkXaXp7hrann`.
- Reading, swapping, or writing Codex CLI `~/.codex/auth.json`.
- Raw token JSON, plaintext credential storage, browser cookie extraction, or session injection.
- Private ChatGPT/Codex backend paths such as `/backend-api/wham/usage`.
- Browser User-Agent impersonation, warm-up traffic, CAPTCHA/MFA bypass, quota circumvention, or automatic rotation intended to evade limits.
- Provider-specific local credential replacement or proxy gateways unrelated to FLASH ROUTER.
- README claims treated as proof of official provider support.

## 7. Architecture mapping into FLASH ROUTER

The reference ideas map to existing feature-first boundaries without changing the Blazor Interactive Server, modular-monolith, SQLite, Vault, or CSS-isolation architecture.

| Reference idea | Existing FLASH ROUTER boundary | Result |
|---|---|---|
| Independent account identity | `AccountProfile` and account workspace ports | Existing account ownership is retained. Email is not an OAuth identity key. |
| Multiple provider connections | `ProviderConnectionRecord`, provider registry, connection UI ports | API, manual web credential, and official OAuth connections remain distinct. |
| Independent OAuth registration | `OAuthRegistrationRecord`, `ConnectionSessionRecord` | Issued client ID, provider subject, scopes, and session metadata are scoped to the registration/connection. |
| Secure token ownership | `TokenVaultReferenceRecord` plus `OAuthTokenVaultStore` | SQLite stores references/metadata; token values stay in the existing Vault. |
| Refresh synchronization | `OAuthConnectionSessionService` | A per-connection semaphore serializes refresh and preserves rotated tokens. |
| Provider-aware usage | `ProviderCapability`, provider adapter registry, observations | Usage is capability- and provider-specific instead of a universal quota assumption. |
| Stale/error presentation | `ProviderObservationStatus`, `ProviderOperationStatus`, latest-observation retention | Failed checks do not turn prior observations into zero or silently delete them. |
| Compact account details | Existing `/accounts` and `/connections` UI-C surfaces | Only compact, source-backed data is eligible for presentation; no new dashboard/sidebar is introduced. |

## 8. New data models

No new production data model was justified by this reference analysis. The required shapes already exist from BE-03/BE-04 and are mapped as follows:

- `OAuthRegistrationRecord`: account, provider, issued client, host identifier, provider subject, agent name, and timestamps.
- `ConnectionSessionRecord`: connection identity, provider subject, issued client, granted scopes, status, expiry, refresh, and verification timestamps.
- `TokenVaultReferenceRecord`: Vault key references only; no access/refresh/ID token values.
- `ProviderConnectionRecord` and `ProviderCredentialReferenceRecord`: connection and credential-kind isolation for API/manual/provider adapters.
- `ProviderCapabilityDefinitionRecord`: provider capability and required credential kind.
- `ProviderCheckRunRecord`, `ProviderObservationRecord`, and `ProviderSyncStateRecord`: check history, provenance, scope, freshness, status, and scheduler state.

The existing model also preserves the important distinctions: `CredentialStored` is not `Authenticated`, OAuth authorization is not subscription verification, and an unknown usage value is not zero.

## 9. Changed files

This addendum changed only:

- `docs/ai/phase-reports/UI-C_REFERENCE_REPOSITORY_INTEGRATION.md`

No application source, test source, migration, Vault file, `.local/security` file, solution file, or frontend structure was changed. This is intentional: the existing implementation already contains the safe subset of the requested concepts, and the remaining gaps require official provider approval or a separately scoped feature decision.

## 10. SQLite migration impact

`NONE` for this addendum.

The existing additive OAuth persistence migration and provider-integration migrations remain the source of the current schema. No destructive migration, data deletion, schema rewrite, or active database replacement was performed. The existing unique indexes continue to protect registration/account ownership and connection identity.

The one future design consideration is multi-connection UI retrieval: `IOAuthConnectionStore.GetForAccountAsync` currently returns the most recently authorized connection for the account. The underlying records support multiple connections, but a future account-details feature would need a separate list query rather than changing this single-status port implicitly.

## 11. Vault and security impact

`NONE` for this addendum; existing protections remain in force.

Verified current boundaries:

- State, nonce, PKCE verifier, exact redirect URI, and attempt expiry are held per pending OAuth attempt.
- `dynamic_agent_client` is used only for the first-registration contract; issued client IDs are retained from the provider callback.
- Callback handling validates state, errors, code, issued client ID, ID-token signature/JWKS, issuer, audience, expiry, nonce, provider subject, and identity scopes.
- Access, refresh, and retained ID tokens are written through `ISecretVault`; SQLite contains only metadata and Vault references.
- Refresh is serialized per connection, rotated refresh tokens are retained, and temporary refresh failures preserve the current session.
- Invalid-grant/reused-refresh-token paths request reauthorization and clear unusable local token material according to the existing contract.
- Disconnect attempts remote revocation through OIDC discovery, clears local Vault material, and reports unconfirmed remote revocation truthfully.

The reference repositories do not justify weakening any of these rules. If future multi-process refresh support is required, add an explicit optimistic token revision/revision check with migration and tests; the current single-process per-connection gate is sufficient for the present desktop-host boundary.

## 12. Build results

- The latest recorded source build before this report was `0 errors` in the existing handoff evidence.
- No new build was run for this report-only change because no production or test source changed.
- The user-reported `MSB3021`/`Access denied` failure targets `bin\\Debug\\net8.0\\FLASH_ROUTER.exe`; the downstream `CS0006` is a consequence of the missing application assembly, not evidence of a source compilation defect. The locked executable must be released by the user-controlled running process before a normal Visual Studio build can copy the apphost.

## 13. Test results

The existing offline evidence remains applicable because the report did not alter executable code:

- Architecture tests: `8/8 PASS`.
- Integration tests: `80/80 PASS` in the latest UI-C/BE regression evidence.
- UI-C account-connection focus: `5/5 PASS`.
- BE-03 OAuth focus includes fresh state/nonce/PKCE, callback replay/expiry, ID-token validation, issued-client validation, scope handling, provider-subject isolation, token rotation, serialized refresh, reauthorization, disconnect, eligibility blocking, loopback validation, Vault isolation, and SQLite integrity/schema checks.
- No live provider credentials, browser cookies, OAuth sessions, or undocumented backend requests were used.

No new test was added because the safe adoption result was report-only. A future multi-connection list query should add synthetic tests for same-email/different-registration identity, per-account isolation, and list ordering before changing the UI port.

## 14. Browser verification

No new browser session or live provider authorization was started for this addendum.

Existing evidence confirms that the local `/accounts` and `/connections` surfaces load in the Blazor shell and present the official OAuth path as blocked/unavailable when eligibility is not confirmed. The UI does not claim OAuth success merely because a browser can be opened. The reference analysis did not add a sidebar, dashboard, or reference-derived provider screen.

Responsive evidence for the broader UI-C work is retained in the existing handoff; this report does not claim a new responsive certification.

## 15. OAuth eligibility

Official ChatGPT OAuth remains `BLOCKED` for live execution.

The current implementation correctly requires application-specific eligibility before starting the flow. No eligibility, issued client ID, partner approval, or live-test authorization was inferred from either community repository. When eligibility is absent, the UI returns a truthful blocked result and does not open a fake success path.

When eligibility is later established, the implementation must still be rechecked against current official OpenAI Docs and the approved application registration. It must use the issued client ID, exact `127.0.0.1` callback contract, fresh state/nonce/PKCE, OIDC validation, and rotating-token handling already represented in the code.

## 16. Official provider capabilities

The provider status is intentionally split by evidence level:

| Capability | Status | Evidence boundary |
|---|---|---|
| OpenAI Platform API | Offline adapter foundation exists | Uses the project’s official-host allowlist and API-key credential boundary; live use still requires an approved key and explicit request scope. |
| OpenRouter API | Offline adapter foundation exists | Capability registry and official-host validation exist; no live key or request was used. |
| Anthropic API | Offline adapter foundation exists | Capability registry and official-host validation exist; no live key or request was used. |
| Gemini API | Offline adapter foundation exists | Capability registry and official-host validation exist; no live key or request was used. |
| DeepSeek/Groq API | Offline adapter foundation exists | Existing provider adapters are bounded to their configured official hosts; no live request was used. |
| ChatGPT official OAuth | Contract/offline validation exists; live `BLOCKED` | Requires application eligibility, issued registration, consent, and explicit live-test approval. |
| Codex Switcher/Cockpit private usage endpoints | Not an official FLASH ROUTER capability | Community code is not provider authorization or an official API contract. |
| Copilot, Cursor, Windsurf, Kiro, Antigravity, Codex CLI | Research only | Official authorization, ownership, supported metadata, and quota APIs must be verified separately. |

## 17. Unsupported and blocked features

### `ADOPT`

- Multi-account identity and account-picker concepts.
- Independent registration/connection ownership.
- Per-connection Vault references.
- Per-connection refresh serialization and rotated-token preservation.
- Explicit authorized, reauthorization, disconnected, unavailable, stale, unsupported, and error states.
- Capability-based observation models with source, scope, provider-reported time, observed time, and validity.
- Safe error messages that do not expose tokens, raw provider bodies, or credentials.

### `ADAPT`

- Compact quota cards rather than a large dashboard.
- Usage windows only when supplied by the provider; no universal five-hour/seven-day assumption.
- Stale cache presentation with a last-known timestamp and source.
- Provider registry/adapters inside the existing Connections module.
- Metadata export and encrypted backup as separate flows; never export raw auth JSON or token values.
- Atomic/compensating persistence patterns within the existing Vault/SQLite boundary.

### `RESEARCH`

- Official GitHub Copilot usage and authorization.
- Official Cursor, Windsurf, Kiro, Antigravity, and Codex CLI integration methods.
- Provider-specific quota windows, credits, subscription expiry, and usage statistics.
- A list-based account-connection port for displaying more than the latest connection.
- Explicit token revision checks if FLASH ROUTER becomes multi-process or multi-instance.

### `REJECT`

- Borrowed OAuth clients, private endpoints, auth-file swapping, credential injection, cookie extraction, warm-ups, quota circumvention, CAPTCHA/MFA bypass, shared account pools, and local proxy gateways unrelated to the product.

### `BLOCKED`

- Live ChatGPT OAuth until official eligibility and an approved application registration are available.
- Any live provider capability without user-provided credentials, explicit scope, official endpoint documentation, and a separately approved live test.
- Any claim of provider quota, credits, plan, or subscription status when the source is absent, stale, unsupported, or not officially documented.

## 18. GO / NO-GO verdict

### Gate results

| Gate | Result | Reason |
|---|---|---|
| REFERENCE-01 - repository audit | `GO` | Required source areas, pinned revisions, behavior, security implications, and license findings are recorded. |
| REFERENCE-02 - data model | `GO` | Existing identity, connection, registration, Vault reference, capability, and observation models cover the safe adopted concepts. |
| REFERENCE-03 - account workflows | `GO OFFLINE` | Manual credential storage and official OAuth UI boundaries are separated; live OAuth remains blocked and no fake success exists. |
| REFERENCE-04 - usage presentation | `GO OFFLINE` | Existing observations carry source/freshness/status; private quota endpoints are not adopted. |
| REFERENCE-05 - provider registry | `GO OFFLINE` | Existing capability registry supports bounded adapters; future providers remain research/unsupported until verified. |
| REFERENCE-06 - security | `GO` | No borrowed client, plaintext token persistence, private endpoint, Vault regression, or permission bypass was introduced. |
| REFERENCE-07 - full regression | `GO WITH EXISTING EVIDENCE` | Existing architecture, integration, OAuth-offline, SQLite, Vault, and UI-C evidence remains valid; no new live claim is made. |

### Final decision

`GO` for independently adopting the safe design principles from both repositories through the existing FLASH ROUTER architecture.

`NO-GO` for copying their implementations or enabling their private provider integrations.

`BLOCKED` for live ChatGPT OAuth and unverified AI IDE integrations.

The next authorized implementation step should be a separately scoped feature for a multi-connection read port and synthetic tests, or an official-provider research phase. Neither should begin by copying community OAuth, auth-file, quota, or credential-injection code.
