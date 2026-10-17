# BE-04C OpenAI Platform Adapter

Date: 2026-10-16  
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Verdict

`BE-04C OPENAI PLATFORM - OFFLINE GO`.

Live status: `NOT VERIFIED`; this is separate from BE-03 ChatGPT OAuth and no OAuth token is treated as an OpenAI Platform billing credential.

## Supported read-only contracts

- `GET https://api.openai.com/v1/models` for non-billable model metadata/health.
- `GET /v1/organization/usage/completions` with a bounded time range and daily bucket.
- `GET /v1/organization/costs` with a bounded time range and daily bucket.

Standard API keys are rejected locally for organization usage/cost operations with `INSUFFICIENT_PERMISSION`. Administrative/project credentials use separate credential kinds and optional `OpenAI-Organization` or `OpenAI-Project` scope headers.

Usage and cost observations preserve time-bucket scope, token/request units, currency, and provider timestamps. Cost is not inferred from token usage.

## Tests

Fixtures cover standard/admin separation, bounded ranges, model metadata, usage buckets, cost buckets, empty periods, malformed responses, rate limiting, cancellation, and credential-provider mismatch. No model inference or mutation endpoint is called.
