# Official OAuth Runbook

## Preconditions

- An approved official client registration exists for FLASH ROUTER.
- Eligibility, redirect URI, scopes, consent, and live-test approval are recorded.
- The test account and provider authorization are explicitly user-approved.

## Safe flow

1. Create a new authorization attempt in FLASH ROUTER.
2. Open or copy only the authorization URL generated for that attempt.
3. The user completes provider login and MFA on the official provider page.
4. Receive a callback only on the exact loopback redirect for the active attempt.
5. Validate state, nonce, PKCE, client identity, expiry, scopes, code response, and provider identity.
6. Store token values only through the encrypted Vault and write metadata/reference rows to SQLite.
7. Show the verified identity and connection state without logging token material.

## Abort conditions

- Eligibility or client registration is missing.
- State, nonce, redirect, client, scope, identity, or expiry validation fails.
- The callback does not belong to an active attempt.
- Provider response or ID-token validation fails.
- The requested operation would require cookie extraction, private endpoints, token swapping, or automated login.

## Current status

Offline contract and security tests are `GO`. Live authorization, token exchange, refresh, revoke, and usage verification are `BLOCKED` / `NOT EXECUTED` in this workspace.

## AUTH-REAL-01 eligibility reassessment

The official OpenAI documentation describes identity-only Sign in with ChatGPT and a separate ChatGPT plan-usage capability. The open-source plan-usage flow is documented for tools that perform eligible AI workloads and requires a persisted `ext_agent_host_id`, dynamic first registration, later reuse of the issued `client_id`, granted-scope validation, ID-token validation, and a real Responses API request before usage can be claimed.

FLASH ROUTER currently manages account metadata, credentials, browser profiles, and offline provider contracts; this audit found no approved application registration or live eligibility evidence for this account-manager scope. The eligibility gate therefore remains `BLOCKED`, and no code path was enabled to bypass it. Identity-only and plan-usage scopes remain separate in the existing implementation.

Official sources reviewed on 2026-10-16:

- `https://developers.openai.com/siwc/quickstart`
- `https://developers.openai.com/siwc/token-sharing-open-source`
- `https://developers.openai.com/siwc/token-sharing-open-source/sign-in`
- `https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions`
- `https://developers.openai.com/siwc/token-sharing-open-source/token-reference`
- `https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery`
- `https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations`

