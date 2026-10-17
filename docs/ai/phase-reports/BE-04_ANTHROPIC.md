# BE-04D Anthropic Adapter

Date: 2026-10-16  
Workspace: `D:\\ĐỒ ÁN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Verdict

`BE-04D ANTHROPIC - OFFLINE GO`.

Live status: `NOT VERIFIED`.

## Scope

The adapter represents Anthropic Platform API access independently from Claude Web and Enterprise subscription data. It uses `x-api-key` and the official `anthropic-version: 2023-06-01` header.

Read-only contracts represented:

- `GET /v1/models` for API health/model metadata.
- Organization usage report messages endpoint for Admin API usage.
- Organization cost report endpoint for Admin API costs.

Input, output, cache-read, and cache-creation token fields remain separate observations. Costs retain provider currency and are not derived from token counts.

## Tests

Synthetic fixtures cover ordinary-key permission rejection, Admin API mapping, daily report buckets, missing values, pagination-shaped data, malformed JSON, and safe authorization failures. No Claude Web scraping or browser session access is used.
