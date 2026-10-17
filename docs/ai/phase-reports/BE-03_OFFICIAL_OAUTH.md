# BE-03 Official OAuth

Date: 2026-10-16

Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Executive summary

BE-03 implements the official Sign in with ChatGPT OAuth contract offline-first. The implementation has no approved application registration for FLASH ROUTER, so live authorization is explicitly blocked and was not attempted.

## Official OpenAI Docs verification

The implementation was checked against the following official OpenAI documentation:

- https://developers.openai.com/siwc/token-sharing-open-source
- https://developers.openai.com/siwc/token-sharing-open-source/sign-in
- https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions
- https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery

Verified contract points:

- OSS plan-sharing uses dynamic first registration with `dynamic_agent_client`, then reuses the issued client ID.
- A stable opaque `ext_agent_host_id` is persisted per host.
- Each authorization attempt receives fresh state, nonce, and PKCE verifier.
- The callback uses the exact `http://127.0.0.1:<port>/auth/callback` loopback shape; `localhost` is not substituted.
- The token exchange uses the issued client ID, authorization code, verifier, exact redirect URI, and resource; no client secret is assumed.
- ID tokens require signature, issuer, audience, expiry, nonce, and provider-subject validation.
- Each registration keeps its own identity, client ID, scopes, and credential references, including when emails match.
- Refresh uses the issued client ID and rotating refresh tokens; refreshes are serialized per connection.
- Disconnect revokes the renewable session when possible and clears local token ciphertext.
- Missing `chatgpt.tokens.use.direct` disables plan usage; it is not treated as proof of a plan or quota.

Eligibility is not established by the repository. No partner approval, OSS qualification evidence, issued client ID, or user authorization was assumed.

## Architecture changes

- Feature-first Connections slices now contain registration/start, callback security, refresh, and disconnect application services.
- State, nonce, and PKCE verifier remain in an in-memory pending-attempt registry and never enter authorization URLs or logs.
- Access, refresh, and retained ID tokens are protected by the existing `ISecretVault`.
- SQLite stores only registration, session metadata, token-vault references, and connection checks.
- Added migration `20261015162504_AddOAuthConnectionPersistence` creates `OAuthRegistrations`, `ConnectionSessions`, `TokenVaultReferences`, and `ConnectionChecks`.
- Existing `.local/data/flashrouter.db` was backed up before the additive migration and successfully migrated in Integrated mode.
- Preview and UI actions remain truthful and do not simulate an authorized provider session.

## Offline implementation status

- OFFLINE IMPLEMENTED: PKCE S256, state/nonce lifecycle, loopback URI validation, callback replay/expiry checks, issued-client validation, ID-token RSA/JWKS validation, scope checks, multi-account subject isolation, vault token storage, SQLite metadata persistence, refresh rotation, disconnect/revocation handling, and truthful UI states.
- OFFLINE TESTED: valid flow, state mismatch, missing/expired state, replay, invalid signature, issuer, audience, expiry, nonce, missing scopes, subject mismatch, refresh rotation/race serialization, invalid grant, disconnect, eligibility blocking, loopback restriction, vault reference isolation, and SQLite integrity/schema checks.
- LIVE NOT VERIFIED: provider registration, browser authorization, callback listener against OpenAI, live JWKS, live token exchange, live refresh, live revocation, or plan usage.
- BLOCKED: live OAuth until FLASH ROUTER eligibility, registration, user consent, and explicit live-test approval are provided.

## Runtime and browser evidence

- Integrated host started on localhost and applied the additive migration.
- `/accounts` and `/connections` loaded in the existing Blazor shell.
- `/connections` displayed “Official OAuth is blocked for live use”.
- The add-account modal exposes “Đăng nhập bằng ChatGPT (Chưa khả dụng)” without a fake success path.
- No Chrome login, live OAuth URL, provider token, or billable request was initiated.

## Known limitations

- The callback listener/browser launcher is represented by a safe loopback contract and offline service boundary; live listener execution remains gated.
- The repository cannot prove whether FLASH ROUTER qualifies for OpenAI OSS or commercial partner capabilities.
- OpenAI may change endpoints, scopes, token fields, or eligibility rules; live rollout requires rechecking official OpenAI Docs.
- Account plan, quota, credits, subscription expiry, history, and ban state are not inferred or claimed.

## Verdict

`BE-03 - OFFLINE GO`

Live capability verdict: `BLOCKED` / `LIVE NOT VERIFIED`.
