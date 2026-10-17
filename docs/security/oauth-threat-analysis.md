# OAuth threat analysis

Connections supports an official provider authorization contract with state, nonce, PKCE, exact 127.0.0.1 loopback validation, ID-token verification, refresh serialization, revoke handling, vault-backed tokens, SQLite metadata, and per-registration isolation. Live authorization remains blocked because this repository does not establish FLASH ROUTER eligibility or hold an approved issued client registration. No browser login or provider request is used by automated tests.

The callback runbook is `docs/runbooks/oauth.md`. A callback is accepted only for the active attempt that generated its state, nonce, PKCE verifier, client identity, exact redirect URI, and expiry. OAuth live status remains `BLOCKED` / `NOT EXECUTED`; the offline contract must not be presented as a completed provider login.
