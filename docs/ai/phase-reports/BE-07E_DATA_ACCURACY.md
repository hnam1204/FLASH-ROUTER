# FLASH ROUTER - BE-07E Provider Data Accuracy

Date: 2026-10-16

## Implemented evidence

Provider observations carry provider ID, connection ID through the snapshot/run, capability, metric name, string value, unit, optional currency, source, scope, provider-reported time, observed time, optional validity time, and verification status. Adapters use invariant decimal parsing for numeric fields and do not turn malformed/missing fields into zero values.

The following offline regression was found and fixed in BE-07:

- A failed or rate-limited run was saved in history with no observations.
- Both readers previously selected that newest run and returned an empty latest snapshot, hiding the last known good values.
- In-memory and SQLite readers now choose the newest run containing observations; failed runs remain in history.
- SQLite latest/history ordering now materializes run metadata before UTC-aware ordering because the SQLite provider cannot translate `DateTimeOffset` ordering directly.

Regression evidence: the full integration suite passed `75/75`, including `InMemoryLatestSnapshotSurvivesFailedCheck` and `SqliteLatestSnapshotSurvivesFailedCheck`.

## Accuracy coverage matrix

| Requirement | Current result | Evidence/limitation |
| --- | --- | --- |
| Decimal parsing | Partial/implemented | Invariant `decimal` parsing and non-negative checks are used by adapters; broad precision tests are not present |
| Currency separation | Partial | OpenAI/DeepSeek/OpenRouter credits carry currency in selected paths; key metadata may have no currency and no cross-currency aggregation exists |
| Unit separation | Partial | Units distinguish tokens, requests, models, credits, currency amounts, and provider headers; provider semantic validation is not live-certified |
| Scope isolation | Implemented offline | Organization, project, billing account, individual key, and model scopes are explicit; no silent cross-scope summing in the sync path |
| Time buckets | Not sufficient | OpenAI/Anthropic metric names suffix a start timestamp, but persisted observations have no explicit bucket end or first-class bucket identity |
| Pagination | Not implemented | OpenAI and Anthropic report adapters do not follow provider pagination in the current source |
| Freshness/stale policy | Not sufficient | `ValidUntilUtc` exists but adapters do not populate a general freshness policy or convert aging snapshots to `Stale` |
| Unknown versus zero | Partial | Missing values are omitted rather than converted to zero; there is no explicit unknown metric record/UI contract |
| Duplicate snapshots | Partial | Each check run is retained and per-run metric names are unique; no reconciliation/deduplication policy exists across overlapping reports |
| Out-of-order reports | Not sufficient | Latest selection uses completion time and does not reject an older provider-reported period arriving later |
| Historical updates | Not tested | No fixture covers provider corrections to a historical bucket |
| Estimated versus actual | Partial | Official and calculated-from-official sources are distinct; there is no dedicated estimated-cost status |
| Failure retention | Fixed and tested | Failed checks no longer hide the prior non-empty snapshot |

## Required follow-up before a full accuracy GO

Add first-class period start/end and provider dimension fields, implement provider-specific pagination, define a freshness policy, add explicit unknown/estimated statuses, and test currency precision, overlapping buckets, duplicate reports, corrected historical buckets, out-of-order responses, and scope mismatch. Then run the suite against provider-approved fixtures and, only with approval, redacted live evidence.

## Verdict

`BE-07E DATA ACCURACY - NO-GO`

The critical stale-snapshot overwrite behavior is fixed, but the required accounting and reconciliation contract is not complete or live-verified.
