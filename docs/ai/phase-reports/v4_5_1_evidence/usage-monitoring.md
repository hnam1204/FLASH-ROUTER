# Usage and Monitoring Evidence

## Status

**PARTIAL - Synthetic provider and deterministic scheduler scope.**

## Evidence

- `AccountUsageReaderTests` passed for usage projection behavior.
- Synthetic provider tests passed for numeric usage parsing, nullable limits, remaining values, and provider-specific observations.
- Unsupported capabilities remain `Unsupported`; missing metrics are not fabricated as zero.
- `ProviderSyncSchedulerTests` passed for cancellation, eligibility, and non-overlapping scheduling behavior.
- Activity sinks and SQLite projections are wired for sanitized usage and scheduler events.

## Not run

- No live provider API credential was authorized or used.
- A real three-cycle, approximately 60-second host observation was not run during this acceptance pass; this gate remains `NOT RUN`.
- ChatGPT personal web quota was not inferred from API usage and no private provider endpoint was called.

Supporting source: `src/Modules/Connections/Application/AccountUsageReader.cs`, `src/Modules/Connections/Features/ProviderIntegration/Verification/ProviderSyncScheduler.cs`, and tests under `tests/FlashAI.IntegrationTests/`.
