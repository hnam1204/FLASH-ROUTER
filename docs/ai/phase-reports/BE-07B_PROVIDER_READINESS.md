# FLASH ROUTER - BE-07B Provider Live Readiness

Date: 2026-10-16

## Readiness boundary

Provider adapters are offline-contract implementations. Their fixed endpoints, capability descriptors, permission checks, response parsing, host allowlist, timeout, response-size bound, redirect policy, retry classification, and vault scoping were audited. None of those facts prove that a real credential is entitled to a capability or that a provider response currently matches the fixture contract.

Every live test in this document is `DISABLED` until the user explicitly approves the provider, internal connection, operation, capability, and request limit. Saving a credential is not approval to use it.

## API provider matrix

| Provider | Implemented official category | Endpoint mapping in source | Credential and permission boundary | Billable/rate risk | Offline status | Live status |
| --- | --- | --- | --- | --- | --- | --- |
| OpenRouter | Identity, key health/metadata, key usage/limits, credits/balance when allowed | `/api/v1/key`; `/api/v1/credits` on `openrouter.ai` | API key for key metadata; credits may require management permission | Read-only metadata; at most 2 GETs; no inference | Fixture tested | `LiveNotVerified` |
| OpenAI Platform | Models; organization usage/tokens; organization costs/billing period | `/v1/models`; `/v1/organization/usage/completions`; `/v1/organization/costs` on `api.openai.com` | Standard key for models; admin/project credential and matching organization/project scope for usage/cost | Usage/cost are sensitive administrative reads; at most 3 GETs; no inference | Fixture tested | `LiveNotVerified` |
| Anthropic Platform | Models; organization usage/tokens; organization costs | `/v1/models`; `/v1/organizations/usage_report/messages`; `/v1/organizations/cost_report` on `api.anthropic.com` | Admin API credential required for usage/cost; separate from Claude Web | Administrative reads; at most 3 GETs; no generation | Fixture tested | `LiveNotVerified` |
| Gemini API | Connection health and model metadata | `/v1beta/models` with `x-goog-api-key` on `generativelanguage.googleapis.com` | Gemini API key; Cloud Monitoring/Billing IAM is not inferred | One metadata GET; no generation | Fixture tested | `LiveNotVerified` |
| DeepSeek API | Balance and credits per returned currency | `/user/balance` with Bearer key on `api.deepseek.com` | API key; provider-reported currency only | Financial metadata read; at most 1 GET | Fixture tested | `LiveNotVerified` |
| Groq API | Models and response rate-limit headers | `/openai/v1/models` on `api.groq.com` | API key | One metadata GET; no generation; rate headers are response metadata | Fixture tested | `LiveNotVerified` |

The adapter descriptors intentionally do not advertise unsupported usage, cost, quota, subscription, or balance capabilities. OpenRouter key metadata values are retained as provider `credits` with no inferred currency; the `/credits` response is the only path that currently labels USD.

## AI Web provider matrix

| Provider | Official flow in this solution | Safe readiness status | Live plan |
| --- | --- | --- | --- |
| ChatGPT | Offline official OAuth registration/callback/security contracts exist; eligibility is disabled by default | `RequiresAuthentication` and `LiveNotVerified` | User-driven official OAuth only; no website scraping |
| Claude Web | No official OAuth/API adapter for personal web-account subscription or usage is implemented | `DocumentationUnverified` | Disabled; no private web endpoint |
| Gemini Web | No official web-account adapter is implemented | `DocumentationUnverified` | Disabled; no cookie/browser-session access |
| GitHub Copilot | No official account-entitlement adapter is implemented | `DocumentationUnverified` | Disabled; no private endpoint |
| Cursor | No official account-entitlement adapter is implemented | `DocumentationUnverified` | Disabled; no private endpoint |
| Antigravity | No official provider flow or adapter is present | `DocumentationUnverified` | Disabled |

ChatGPT Web subscription/plan data must not be inferred from OpenAI Platform API usage. Claude Web, Gemini Web, Copilot, Cursor, and Antigravity remain outside the implemented official capability boundary.

## Disabled live test catalog

| Test ID | Provider | Capability | Max requests | Cost/rate posture | Success criteria | Failure criteria and cleanup |
| --- | --- | --- | ---: | --- | --- | --- |
| `LR-OR-KEY-01` | OpenRouter | Current key metadata | 1 | No inference; one GET | Safe metadata response matches the approved connection | 401/403/429/invalid response; retain prior snapshot; no credential output |
| `LR-OR-CREDITS-01` | OpenRouter | Credits/balance | 1 | Requires explicit management permission; no inference | Provider returns current credit fields in the approved scope | Permission failure is `RequiresPermission`, not zero; no cleanup beyond redacted evidence |
| `LR-OAI-MODEL-01` | OpenAI Platform | Model availability | 1 | Non-billable GET | Models response is valid and scoped | Auth/permission/error mapping; no inference |
| `LR-OAI-USAGE-01` | OpenAI Platform | Usage/tokens | 1 | Admin/project read; no inference | Approved range returns correctly scoped buckets | Permission, pagination, stale, or malformed response is not converted to zero |
| `LR-OAI-COST-01` | OpenAI Platform | Costs/billing period | 1 | Administrative read; no inference | Currency and period are provider-reported | Missing currency/period blocks verified display |
| `LR-ANT-MODEL-01` | Anthropic | Model availability | 1 | Non-billable GET | Valid model metadata | Safe failure mapping |
| `LR-ANT-REPORT-01` | Anthropic | Usage/cost report | 1 per approved capability | Admin read; no generation | Correct organization scope and period | Permission/reporting delay/pagination failure; preserve old snapshot |
| `LR-GEM-MODEL-01` | Gemini API | Model availability | 1 | One GET; no generation | Valid model metadata | Invalid key or response; no quota inference |
| `LR-DS-BAL-01` | DeepSeek | Balance/credits | 1 | Financial metadata read | Currency and balances are provider-reported | Unknown currency blocks verified amount display |
| `LR-GROQ-MODEL-01` | Groq | Models/rate headers | 1 | One GET; no generation | Model response and optional headers parse safely | Missing headers remain unsupported, not zero |
| `LR-CHATGPT-OAUTH-01` | ChatGPT | Official OAuth identity | One browser authorization flow | User consent required; no credentials in chat | Valid state, nonce, PKCE, issuer, audience, and identity match | Cancel/replay/wrong identity/expired token; revoke or disconnect only when separately approved |

The live approval record must include provider ID, internal connection ID or masked identifier, operation, capability set, scope, maximum requests, cost posture, timeout, and evidence retention. It must not include secrets, authorization headers, cookies, or raw sensitive responses.

## Verdict

`BE-07B LIVE READINESS - GO`

The readiness architecture has a bounded provider matrix and disabled per-provider plans. It is not a live certification and does not claim that any provider currently works with a real account.
