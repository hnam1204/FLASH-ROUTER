# BE-03L Official OAuth Callback Certification

Date: 2026-10-16
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Verdict

`GO OFFLINE`. `BLOCKED LIVE` until FLASH ROUTER has an approved official Sign in with ChatGPT client registration, confirmed eligibility, user consent, and explicit approval for a live authorization test.

## Implemented contract

- Authorization attempts are created by FLASH ROUTER and have isolated state, nonce, PKCE verifier, exact loopback redirect URI, client identity, and expiry.
- Callback validation rejects replay, state mismatch, wrong client, wrong redirect, expired attempts, invalid code responses, missing scopes, invalid identity, and invalid ID tokens.
- Verified provider identity is the source of OAuth subject data; account email is never accepted as a substitute provider subject.
- Access, refresh, and retained ID tokens are written only through the existing Vault boundary. SQLite stores registration, session, status, scope, identity metadata, and Vault references.
- Refresh and disconnect operations are serialized per connection and revoke handling remains explicit.
- The UI exposes truthful eligibility and callback states and does not report success merely because an authorization URL exists.

## Security boundary

- Loopback binding is restricted to `127.0.0.1` and exact redirect validation.
- No Codex CLI OAuth client, private endpoint, token swapping, cookie extraction, or browser session import is used.
- Tests use synthetic authorization codes, identities, keys, and token clients only.
- No provider network call, browser login, MFA flow, or token exchange was executed in this certification.

## Evidence

- Targeted `Be03OAuthTests` result: `9/9` passed.
- Existing database integrity and OAuth metadata schema check: `1/1` passed.
- Full solution regression: architecture `8/8`, integration `95/95`.
- Disposable database migration copy passed without changing the source database file.

## Live gate

The following are intentionally not claimed:

- official client registration or eligibility;
- successful provider authorization;
- callback receipt from a real provider;
- token exchange, refresh, revocation, or identity verification against a live provider;
- ChatGPT plan or usage data.

