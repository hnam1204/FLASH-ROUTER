# Provider Capability Matrix

This matrix describes implemented adapter boundaries and readiness status. It is not evidence that a real credential currently works. Unsupported capabilities are returned as unsupported or permission-required states; they are never represented as zero values.

## AI API providers

| Provider | Supported offline capabilities | Official source/endpoint category | Credential boundary | Live status |
| --- | --- | --- | --- | --- |
| OpenRouter | Connection health and key usage/limits for a standard API key; credits/balance only for a credential with the required management permission | `openrouter.ai` `/api/v1/key`, `/api/v1/credits` | Standard API key: key endpoint; management credential: credits/balance when permitted | `LiveNotVerified` |
| OpenAI Platform | Connection health, models, organization usage/tokens, organization costs/billing period | `api.openai.com` `/v1/models`, `/v1/organization/usage/completions`, `/v1/organization/costs` | Standard key for models; admin/project credentials for reports with matching scope | `LiveNotVerified` |
| Anthropic Platform | Connection health, models, organization usage/tokens, organization costs | `api.anthropic.com` `/v1/models`, organization usage/cost report categories | Admin API for reports; separate from Claude Web | `LiveNotVerified` |
| Gemini API | Connection health, model metadata | `generativelanguage.googleapis.com` `/v1beta/models` | Gemini API key; Cloud IAM and billing are not inferred | `LiveNotVerified` |
| DeepSeek API | Connection health, balance, credits per returned currency | `api.deepseek.com` `/user/balance` | API key; currency remains provider-reported | `LiveNotVerified` |
| Groq API | Connection health, models, response rate-limit headers | `api.groq.com` `/openai/v1/models` | API key; no billing or usage inference | `LiveNotVerified` |

All six adapters use fixed official HTTPS base addresses, a 20-second client timeout, disabled redirects, a bounded JSON response, safe error mapping, provider-specific credential scope, and fixture-only tests in this phase.

## AI Web providers

| Provider | Supported official flow | Capability boundary | Live status |
| --- | --- | --- | --- |
| ChatGPT | Manual web credential metadata, isolated official-site browser profile, and offline official OAuth/security contracts | Manual credential is `LocalMetadata` / `WebSessionUnknown`; verified identity requires official OAuth or another supported verification source; no inferred plan/usage | `LiveNotVerified` |
| Claude Web | None | No private web endpoint or cookie scraping | `DocumentationUnverified` |
| Gemini Web | None | No private web endpoint or cookie scraping | `DocumentationUnverified` |
| GitHub Copilot | None | No entitlement adapter | `DocumentationUnverified` |
| Cursor | None | No entitlement adapter | `DocumentationUnverified` |
| Antigravity | None | No official flow or adapter in this solution | `DocumentationUnverified` |

The registry also exposes the following research-only metadata entries. They are intentionally not connectable, have no adapter, and cannot report a successful check:

| Provider | Registry state | Connectable | Live status |
| --- | --- | --- | --- |
| GitHub Copilot | `ResearchOnly` | No | `NotVerified` |
| Cursor | `ResearchOnly` | No | `NotVerified` |
| Windsurf | `ResearchOnly` | No | `NotVerified` |
| Kiro | `ResearchOnly` | No | `NotVerified` |
| Antigravity | `ResearchOnly` | No | `NotVerified` |
| Codex CLI | `ResearchOnly` | No | `NotVerified` |

ChatGPT Web subscription/plan data is not inferred from OpenAI Platform API data. No web provider is marked as supported merely because the user can log in through a browser.

AUTH-REAL-01 adds an explicit distinction between `UserProvidedIdentity`, `StoredCredential`, `BrowserOpened`, `WebSessionUnknown`, `BrowserSessionObserved`, `OAuthAuthorized`, and `VerifiedIdentity`. A manual ChatGPT web credential never receives `ProviderApiCredential` provenance and never populates `VerifiedIdentity`. AUTH-BROWSER-01 browser observations remain visible-UI evidence only; subscription, usage, quota, and credits stay unsupported unless an official authorized source is added.

For OpenRouter, capability resolution intentionally removes `Credits` and `Balance` from a standard `ApiKey` connection while retaining official key usage/limit support. The matrix must not claim management credit access merely because `/api/v1/credits` exists for a different credential class.

LIVE-ACCEPTANCE-03 confirms this matrix offline only. No provider API request was executed; OpenRouter is `READY / NOT EXECUTED` for a future connection-specific read-only check. ChatGPT Web plan, quota, credits, and usage remain unsupported for automatic import and must be viewed on the official site when needed.

## LIVE-ACCEPTANCE-03 provider evidence

- The six implemented API adapters passed the offline capability, credential-scope, endpoint, response-normalization, error-mapping, and observation-persistence checks included in the regression suite.
- A standard OpenRouter API key retains official key usage/limit capability. Credits and balance remain management-permission capabilities and are not inferred from endpoint availability.
- No connection was selected for a live check, no Vault credential was resolved for a live request, and the request count is `0`.
- The correct live status is `READY / NOT EXECUTED`, not zero usage and not verified provider data.
- ChatGPT Web remains a separate browser surface; successful website login cannot authorize OpenAI Platform organization usage or prove plan, quota, credits, or billing.

## Capability semantics

The shared capability enum includes identity, authorization/connection health, subscription, usage, token usage, costs, credits, balance, quota, rate limits, model availability, and billing period. Resolution removes administrative/billable capabilities for credentials that do not meet the adapter boundary and applies Gemini-specific restrictions. Live eligibility remains a separate approval gate.

See `docs/ai/phase-reports/BE-07B_PROVIDER_READINESS.md` for endpoint-specific disabled live plans and evidence requirements.
