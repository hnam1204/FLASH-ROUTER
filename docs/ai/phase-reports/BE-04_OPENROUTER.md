# BE-04B OpenRouter Adapter

Date: 2026-10-16  
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Verdict

`BE-04B OPENROUTER - OFFLINE GO`.

Live status: `NOT VERIFIED`; no OpenRouter key or live request was used.

## Official endpoints represented

- `GET https://openrouter.ai/api/v1/key` for current-key metadata.
- `GET https://openrouter.ai/api/v1/credits` for management-permission credit data.

The adapter maps only fields present in the response: label, usage, daily/weekly/monthly usage, limit, remaining limit, reset, expiration, total credits, and total usage. Remaining credits are marked `CalculatedFromOfficialObservations` when both source values are present and valid.

## Credential and permission behavior

The API key remains in the vault. A 403 from the credits endpoint becomes a safe permission result; it does not invalidate the connection or convert credits to zero. Individual-key usage is not presented as organization spending.

## Tests

Synthetic fixtures cover nullable fields, decimal precision, credits calculation, 401/403/429 classification, malformed JSON, negative values, and secret-safe error text. No inference endpoint is called.
