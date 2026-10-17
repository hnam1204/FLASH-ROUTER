# LIVE-USAGE-01 Provider Data Acceptance

Date: 2026-10-16  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`

## Verdict

`PARTIAL`.

Provider capability modeling, offline adapters, permission-aware OpenRouter resolution, observation boundaries, and read-only UI behavior are GO offline. Live provider usage, quota, credits, billing, and account entitlement checks are `NOT EXECUTED`.

## Capability boundary

The implementation keeps provider capabilities separate from browser login state and from credential storage. A ChatGPT web browser session does not authorize OpenAI Platform usage endpoints. A stored API key does not prove identity, subscription, quota, or billing access.

The six API adapters remain isolated and fixture-tested: OpenRouter, OpenAI Platform, Anthropic, Gemini, DeepSeek, and Groq. Web providers without an official supported endpoint remain unsupported or research-only; no private endpoint, cookie, local-storage value, or browser token is scraped.

## OpenRouter correction

`src/Modules/Connections/Features/ProviderIntegration/Registry/ProviderRegistry.cs` now keeps standard API-key capability resolution for the official `/api/v1/key` endpoint while removing management-only `Credits` and `Balance` capabilities from a standard key. Management credentials may expose those capabilities only when their credential kind and adapter contract permit them.

The adapter continues to use the official HTTPS host and bounded read-only requests. It does not put keys in URLs, log raw responses, perform an inference request, or convert a missing permission into zero. The UI refresh action in `src/Modules/Connections/Features/ManageConnections/UI/ConnectionTab.razor` is read-only and reports the actual result/freshness state.

## Offline evidence

| Check | Debug | Release |
| --- | ---: | ---: |
| Architecture tests | 8/8 | 8/8 |
| Full integration tests | 127/127 | 127/127 |
| Solution build | 0 warnings, 0 errors | 0 warnings, 0 errors |

Provider fixtures cover official endpoint routing, nullable usage/limit fields, credit parsing, rate limits, malformed responses, unsupported capabilities, credential scope, observation persistence, and no-secret leakage. The OpenRouter regression explicitly verifies that a standard key retains key-usage support but does not claim management credits or balance permissions.

## Live provider status

No live key, OAuth token, browser session, provider account, billing endpoint, or billable inference operation was used. No provider API request was made in this acceptance phase.

| Provider/gate | Result |
| --- | --- |
| OpenRouter official read-only check | READY FOR LIVE - NOT EXECUTED |
| OpenAI Platform usage/cost | NOT EXECUTED; admin/project scope required where applicable |
| Anthropic usage/cost | NOT EXECUTED; administrative scope required where applicable |
| Gemini usage/billing | NOT EXECUTED |
| DeepSeek balance | NOT EXECUTED |
| Groq usage/billing | NOT EXECUTED |
| ChatGPT web subscription/quota | NOT EXECUTED and not inferred |
| Tier 5 overall | PARTIAL |

Any future live check must be explicitly user initiated for one named provider and one connection, use a Vault-selected credential, show the endpoint/capability/scope, make the smallest bounded read-only request, and persist only normalized observations and freshness metadata. A standard OpenRouter key must not be presented as a management credential.

## Safety and data preservation

- No credential was copied into this report or used in a request.
- No SQLite account row, Vault ciphertext, DPAPI/Data Protection key, browser profile, cookie, token, or provider response was changed.
- Unsupported, unauthorized, rate-limited, stale, and unavailable states remain distinct from zero usage.
- ChatGPT web plan and usage are not inferred from OpenAI Platform API data.
- The previously pasted test credential was not read or tested; if real, rotate it before any future live test.

