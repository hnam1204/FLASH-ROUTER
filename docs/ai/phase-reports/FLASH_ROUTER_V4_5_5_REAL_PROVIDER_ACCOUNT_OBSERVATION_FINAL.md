# FLASH ROUTER V4.5.5 - Real Provider Account Observation Final Report

Date: 2026-10-18
Branch: `feat/account-automation-v4-4-recovery`
Classification: `PARTIAL`

## Scope and Baseline

This phase extends the V4.5.4 authenticated-browser and profile-promotion flow. The V4.5.4 identity and profile-promotion invariants remain the baseline:

- The stored account email is never overwritten by browser observations.
- An authenticated shell or display name is not treated as email identity proof.
- Only an exact normalized email match can authorize promotion.
- A mismatch blocks promotion.
- Missing identity evidence remains unverified/pending.
- The original browser profile remains protected while a candidate session is inspected.

The worktree already contained the V4.5.4 implementation and report. This phase added the account-observation pipeline, UI projection, persistence, synthetic Playwright coverage, and the migration needed for durable provenance.

## Root Cause of `IDENTITY_EMAIL_UNAVAILABLE`

The previous flow proved that the authenticated ChatGPT shell was present and that an account control existed, but it treated the visible account-menu text as the complete identity source. The provider UI can expose the authoritative email only after opening the account menu and navigating to the official Settings/Account surface. Consequently, the earlier extractor stopped after shell/menu detection and returned `IDENTITY_EMAIL_UNAVAILABLE` even though the authenticated session was usable.

The corrected flow performs bounded, read-only discovery in this order:

1. Select the authenticated account control on the owned Playwright context.
2. Open the visible account menu and inspect its text for email/display name evidence.
3. Locate Settings using English/Vietnamese semantic names, then bounded test-id/ARIA fallbacks.
4. Wait for a real Settings surface after the click; menu buttons and links are rejected as false Settings surfaces.
5. Select the Account section when present and read only the visible Settings surface.
6. Extract email, display name, plan, and supported MFA status from that surface.
7. Mark quota as unsupported when no supported ChatGPT Web source is visible.
8. Close transient account/settings surfaces with Escape in a best-effort cleanup path.

## Provider UI Surfaces

The implementation is restricted to user-facing DOM surfaces and does not call private provider APIs or inspect cookies, storage, headers, tokens, conversations, or credentials.

The supported ChatGPT surface model is:

- Authenticated account control: provider-specific `data-testid` or account/profile/user-menu ARIA labels.
- Account popover: visible `[role=menu]` or account/profile menu test-id surface.
- Settings entry: semantic `Settings`, `Cai dat`, `Account settings`, or `Cai dat tai khoan`, with bounded semantic attribute fallbacks.
- Settings surface: visible dialog/settings surface that is not a button, link, or menu.
- Account section: semantic `Account` or `Tai khoan` control when the Settings UI exposes sections.
- Field text: only the selected account/settings surface is read.

The synthetic fixtures exercise both English and Vietnamese labels, including delayed Settings rendering. They demonstrate `Plan: Plus` with `MFA configuration: Enabled` and `Goi: Free` with `Bao mat: Da bat MFA`. These are safe test values, not live-provider evidence.

No screenshot artifact was generated from the synthetic fixtures in this phase; therefore this report does not claim a screenshot as live or fixture evidence. The fixture DOM and assertions are in `tests/FlashAI.IntegrationTests/BrowserAuthPlaywrightE2ETests.cs`.

## Observation and Provenance

The structured observation model records one overall observation plus independent field evidence for:

- Display name
- Email
- Plan
- MFA configuration
- Usage/quota

Each field carries source, evidence type, confidence, status, observation time, and an optional safe failure reason. Examples include `OfficialSettingsAccount`, `OfficialAccountOrPlanUI`, `OfficialSecuritySettings`, `Observed`, `Unavailable`, and `Unsupported`.

The following safe diagnostics are used where applicable: `ACCOUNT_SETTINGS_UNAVAILABLE`, `IDENTITY_EMAIL_NOT_EXPOSED`, `IDENTITY_EMAIL_MISMATCH`, `PLAN_LABEL_UNAVAILABLE`, `MFA_CONFIGURATION_UNAVAILABLE`, `PROVIDER_UI_CHANGED`, `OBSERVATION_TIMED_OUT`, `OBSERVATION_CANCELLED`, and `UNSUPPORTED_USAGE_SOURCE`.

Plan extraction uses bounded labels for Free, Go, Plus, Pro, Business, and Enterprise. MFA challenge observation remains separate from MFA configuration observation: a successful TOTP step is not treated as proof of the complete configured MFA state.

## Identity Comparison Policy

The comparison normalizes only defensible formatting differences: surrounding whitespace is trimmed, the value must parse as a complete email address, and comparison is case-insensitive using the normalized address. Plus aliases, dotted local parts, and alternate domains are not collapsed into equivalence.

The stored account email remains the source of intent. The provider-observed email is stored as evidence only. The result is:

- `IdentityVerified` when normalized emails match exactly.
- `Mismatch` / `IDENTITY_EMAIL_MISMATCH` when both are available and differ.
- `Unverified` with `IDENTITY_EMAIL_NOT_EXPOSED` or `IDENTITY_EMAIL_UNAVAILABLE` when the provider does not expose a comparable email.
- `Unsupported` when the provider adapter does not support this observation contract.

Display name, plan, MFA, and an authenticated navigation shell never independently promote identity.

## Candidate Promotion Trace

After `AuthenticatedSessionConfirmed`, the orchestrator invokes the account observation contract on the already-owned candidate context before making the final promotion decision. The observation is persisted before the result is projected.

- Exact matching email: observation is persisted, identity becomes verified, and the existing eligibility rules may approve candidate promotion.
- Different email: observation is persisted, identity becomes mismatch, and promotion is blocked without altering the original profile.
- Missing/unavailable email: observation is persisted with an unavailable field, identity remains unverified/pending, and promotion is not authorized.
- Closed or failed context: the observation reports a bounded safe failure; the original profile remains intact and candidate cleanup follows the existing bounded lifecycle.
- A later read-only refresh reuses the active authenticated browser session when available; it does not resubmit credentials or TOTP merely to collect account fields.

## UI Projection

The Account Inspector now separates:

- Stored email and observed provider email.
- Exact identity status and evidence source/time.
- Observed plan and its provenance.
- MFA step/configuration and browser authentication state.
- Profile promotion state.
- Usage/quota data with an explicit unsupported message rather than a guessed value.

The consent-based action `Kiem tra du lieu tai khoan` triggers a bounded read-only collection pass and displays a specific failure reason when collection is unavailable. Existing account lifecycle actions remain unchanged.

## SQLite Persistence

The new migration `20261018150144_AddProviderAccountObservationPersistence` creates:

- `ProviderAccountObservations` for account/provider/session-level observation metadata.
- `ProviderAccountObservationFields` for per-field values and provenance.
- A unique observation-field index and an account/provider/time query index.

The SQLite store writes the observation and all field evidence in one context operation. Reads select bounded candidates and sort `DateTimeOffset` values in memory because SQLite text ordering is not reliable for all offset representations. The in-memory store mirrors the same latest-observation contract.

`dotnet ef migrations list --project 'FLASH ROUTER/FLASH ROUTER.csproj' --startup-project 'FLASH ROUTER/FLASH ROUTER.csproj' --no-build` discovered the new migration and the preceding archived-profile migration. The configured local database reported all listed migrations as pending; disposable integration databases apply the migration set during test setup. No production database migration was applied by this task.

## Test Evidence

Actual results recorded from the worktree:

| Suite | Result |
| --- | ---: |
| Focused provider observation tests | 10/10 passed |
| New headed Settings cases (English, unavailable, delayed Vietnamese) | 3/3 passed |
| Debug non-headed IntegrationTests | 219/219 passed |
| Debug ArchitectureTests | 11/11 passed |
| Debug Playwright mock suite excluding the 60-second case | 15/15 passed |
| Debug 60-second Playwright case | 1/1 passed |
| Release non-headed IntegrationTests | 219/219 passed |
| Release Playwright suite excluding the 60-second case | 15/15 passed |
| Release 60-second Playwright case | 1/1 passed |
| Post-fix targeted headed Release cases | 3/3 passed |

The targeted post-fix Debug provider observation run also completed with 10/10 passed. Builds emitted existing analyzer warnings but no compilation or test failures.

The synthetic coverage includes matching and mismatching identity, display-name/email-unavailable behavior, unavailable Settings, delayed Settings navigation, Free and Plus plan extraction, MFA available/unavailable states, Vietnamese/English labels, browser closure, concurrent account isolation, candidate protection, promotion/mismatch behavior, read-only refresh without authentication replay, SQLite provenance persistence, and absence of secret/session-data logging in the covered paths.

## Unsupported Metrics and Live Gate

Exact ChatGPT Web usage/quota percentages and reset values remain unsupported because no approved official account surface exposed a reliable metric in this implementation. The system records `Status = Unsupported`, `Value = null`, and `Reason = UNSUPPORTED_USAGE_SOURCE`; it does not substitute OpenAI API Platform usage or render estimates.

No live provider account or authorized live browser session was used for this phase. No live credentials, TOTP, cookies, tokens, browser storage, private endpoints, payment data, recovery codes, or conversations were accessed.

`LIVE_PROVIDER_VALIDATION_NOT_RUN`

## Final Classification

`PARTIAL`

The production pipeline, UI-only observation logic, provenance persistence, candidate safety rules, and synthetic Debug/Release coverage are implemented and passing. The classification remains `PARTIAL` because genuine live-provider validation was not explicitly authorized or run, and quota remains unsupported.
