# LIVE-ACCEPTANCE-02 Provider Usage Certification

Date: 2026-10-16  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`

## Verdict

`PARTIAL`.

The capability registry, six existing API adapters, permission-aware OpenRouter behavior, read-only observation persistence, UI refresh boundary, and unsupported ChatGPT Web behavior are GO offline. A real provider request was not executed because no connection-specific live approval and no fresh authorized key were provided.

## Capability audit

The existing adapters remain the only connectable API providers: OpenRouter, OpenAI Platform, Anthropic, Gemini, DeepSeek, and Groq. Research-only providers remain non-connectable and have no fabricated live adapter. Provider capabilities are resolved from provider, connection kind, credential kind, and official endpoint contracts.

`src/Modules/Connections/Features/ProviderIntegration/Registry/ProviderRegistry.cs:77-103` removes OpenRouter `Credits` and `Balance` for a standard API key while retaining official key usage/limit support. `AdminApiKey` and `ProjectApiKey` are the only credential classes allowed to retain those management-level capabilities in the current resolver.

`src/Modules/Connections/Features/ManageConnections/UI/ConnectionTab.razor:236-270` exposes capability-aware, user-initiated checks. It excludes costs/billing from the ordinary refresh action and stores normalized provider observations rather than raw responses. ChatGPT Web subscription, quota, credits, and plan are not inferred from an opened browser or OpenAI Platform API metadata.

## Offline evidence

| Check | Debug | Release |
| --- | ---: | ---: |
| Architecture tests | 8/8 | 8/8 |
| Integration tests | 127/127 | 127/127 |
| Solution build | 0 warnings, 0 errors | 0 warnings, 0 errors |
| OpenRouter standard-key capability regression | PASS | PASS |

Fixtures cover official endpoint routing, key usage/limit parsing, nullable fields, credit parsing, rate limits, malformed responses, unsupported capabilities, provider/connection credential scope, observation normalization, and no-secret leakage. No fixture is treated as live provider evidence.

## Live API status

No provider URL was called in this phase. Request count: `0`. No API key, OAuth token, browser cookie, billable inference, billing endpoint, or organization usage request was used.

| Gate | Result |
| --- | --- |
| LU-02A provider capabilities | GO offline |
| LU-02B OpenRouter read-only request | READY / NOT EXECUTED |
| LU-02B OpenAI/other API usage | NOT EXECUTED |
| LU-02C ChatGPT Web usage/quota | UNSUPPORTED for automatic import; official website view only |
| LU-02D normalized observation persistence | GO offline |
| Overall Tier 5 | PARTIAL |

A future live check must select one existing connection, display provider/capability/permission/scope, require explicit user approval, resolve the Vault credential locally, make one bounded HTTPS read-only request to the official host, save only normalized observations and freshness metadata, and stop. `401`, `403`, `429`, timeout, invalid response, unsupported, and unknown states must remain distinct; none means zero usage.

## Data provenance and preservation

`UserProvided`, `StoredCredential`, `BrowserOpened`, `BrowserSessionObserved`, `OAuthAuthorized`, and provider API observations remain separate provenance states. A ChatGPT browser session does not authorize OpenAI Platform organization usage. A standard OpenRouter key does not prove management credit access.

The real SQLite database, Vault, DPAPI/Data Protection key ring, existing accounts, and browser profiles were not modified. No plaintext credential or token appears in this report. The previously pasted credential was not used and should be rotated if real.

