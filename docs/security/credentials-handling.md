# Credentials handling

AUTH-BROWSER-02 repeats this boundary for headed automation: the Playwright certification uses synthetic account values, a disposable encrypted Vault, and a local mock origin. It never imports a real browser profile, calls a provider API, performs live login, or reads browser cookies, local storage, tokens, or screenshots.

No real credentials are embedded in source, tests, logs, screenshots, or ordinary CSV exports. Integrated mode accepts only explicitly user-authorized local credential input for the encrypted vault; values are not copied into AccountProfiles, ProviderConnections, ordinary ViewModels, diagnostics, or provider requests. Passwords, access tokens, refresh tokens, callback codes, TOTP seeds, and provider cookies remain out of scope for live provider automation in BE-02.

UI-E accepts one-line credential input only for an explicitly user-authorized local save. The parser keeps values in memory, supports quoted delimiters, rejects ambiguous input and temporary OTP codes, and writes secrets through the Vault before committing only references and metadata to SQLite. Basic copy is metadata-only. Saved credential copy requires Vault unlock plus explicit user confirmation and is returned as a transient result; it is not logged, persisted, or sent to a provider.

AUTH-BROWSER-01 adds a separate Vault-gated read path for the selected account's manual ChatGPT web credential. Account ownership is checked against `AccountId` and the stored account email before a password or TOTP value can reach the browser field. The browser-auth attempt stores no credential, TOTP seed, generated code, cookie, local-storage value, session token, callback code, page HTML, or screenshot.

Email, password, and TOTP fills each require a distinct user confirmation. The browser adapter uses stable semantic/attribute locators, validates the official HTTPS origin before each fill, blocks repeated automatic fills, and stops for CAPTCHA, SSO, passkey, recovery, SMS/email, or unknown challenges. The local TOTP contract reuses the existing BE-02 RFC 6238 generator; no external OTP service is called.

The supplied credential in the user message was not read, copied, stored, tested, or included in fixtures. If it represents a real account, rotate its password and TOTP secret because it was exposed in chat.

## Live acceptance gate

Real ChatGPT login, real provider usage, quota, credits, billing, and session-retention checks are separate user-initiated gates. Offline tests use synthetic values, a disposable Vault, and a local mock origin. A live attempt must identify one account/provider, show the official origin and requested capability, require local consent, and stop for CAPTCHA, passkey, SSO, MFA, recovery-code, or unknown challenges. No credential may be pasted into Codex output, a terminal command, a report, or a test fixture.

LIVE-ACCEPTANCE-02 executed no live request (`0` ChatGPT/API requests). The disposable host and browser UI were exercised without creating an account or entering any secret. The real integrated data root was never selected for this runtime acceptance.

## LIVE-ACCEPTANCE-03 certification boundary

The current acceptance pass adds no real credential use. Controlled headed browser tests use synthetic values, a local mock origin, disposable SQLite/Vault roots, and local RFC 6238 generation. The real ChatGPT site was not opened, the Vault was not unlocked for a live account, and no provider API URL was called.

The user-pasted credential is not a test fixture and was not read, copied, stored, or tested. If it belongs to a real account, rotate the password and TOTP seed. Do not paste replacement secrets into Codex, reports, terminals, logs, screenshots, or source files.

The only acceptable future live operation is a user-initiated, single-account action approved inside FLASH ROUTER after the UI displays the masked account, provider, profile binding, destination, requested sensitive step, and consent scope. The browser flow must stop for CAPTCHA, SSO, passkey, MFA, recovery, email/SMS, device approval, or unknown states. A visible browser session remains distinct from verified identity, subscription, usage, quota, credits, and API authorization.

LIVE-ACCEPTANCE-03 evidence: real ChatGPT login `NOT EXECUTED`; real session persistence `NOT EXECUTED`; provider request count `0`; offline regression builds and tests pass. No cookies, local storage, browser tokens, session keys, raw HTML, screenshots, API keys, passwords, or TOTP values were exposed.

## AUTH-BROWSER-06 live credential gate

The public route is certified only through `https://chatgpt.com/` and the provider's own login CTA. The user-pasted account values were not read, copied, stored, tested, or used. If the account is real, rotate its password and TOTP secret and update the local Vault before any future attempt. Replacement secrets must not be pasted into Codex, reports, terminals, screenshots, fixtures, or source.

Real password/TOTP filling requires the selected account, account ownership, unlocked Vault, official origin, and a separate in-app confirmation for each action. Until those conditions are explicitly completed by the user, authenticated login and session persistence are `WAITING FOR USER`, while all mock tests remain synthetic and local.

## AUTH-BROWSER-07 certification boundary

The current offline certification passed the full Debug and Release architecture/integration suites (`8/8` and `142/142`), the complete host DI composition with validation enabled, and disposable HTTP/HTTPS runtime smoke checks. The browser path reached only the official ChatGPT landing page, its official login CTA, and the official email form. It did not submit a password or TOTP.

The previously pasted account values remain compromised and were not read, copied, stored, tested, or placed in any report, fixture, log, screenshot, or command. Before a future live attempt, rotate the password, reset/re-enroll TOTP, update the local Vault, invalidate the old credential, and approve the exact account and sensitive action in FLASH ROUTER. Without those steps, real login, authenticated-session observation, and session-persistence acceptance remain `WAITING FOR USER`.

## AUTH-BROWSER-08 one-click Auto Login boundary

Auto Login adds a one-time, in-memory consent object rather than a durable permission. The consent is bound to one attempt, account, provider, and managed browser-profile binding; it expires, can be revoked, and cannot authorize a second concurrent run or another account. It contains no plaintext credential.

Preflight checks expose only masked account metadata, Vault unlock state, credential-reference readiness, profile status, and the official destination. Password and TOTP values are not read during preflight. During the approved attempt, the credential reader verifies account ownership and Vault access immediately before the corresponding official-origin fill. TOTP is generated locally and is never logged, persisted, copied to the clipboard, or sent to a non-official origin.

The workflow rechecks the allowlisted origin before every sensitive operation and stops for CAPTCHA, passkey, SSO, recovery, SMS/email, rate limiting, unknown challenges, browser closure, or consent expiry. Cancellation revokes consent and releases browser ownership without deleting persistent profile data. No cookies, local storage, tokens, raw HTML, screenshots, or provider-private API calls are used.

Synthetic tests use disposable SQLite, Vault, DPAPI/Data Protection keys, managed profiles, and local mock authentication only. The previously pasted account values were not read, copied, stored, tested, or included in source, fixtures, logs, screenshots, or reports. If those values were real, rotate the password and TOTP secret before updating the local Vault. Real credential acceptance and real session persistence remain `WAITING FOR USER / BLOCKED` until that rotation and explicit in-app approval occur.

## CONSOLE-01 one-shot input

The root AI Auto Login Console uses a distinct memory-only boundary for a single attempt:

- `CredentialLineCodec` accepts only a bounded single line with explicit escaped delimiters. It rejects malformed input and six-digit OTP values, and validation events never echo secret fields.
- `EphemeralCredentialContext` is passed directly to the ephemeral browser-auth orchestrator, cleared in `finally`, and not projected into `AccountProfile`, ordinary ViewModels, SQLite, Vault, logs, or clipboard output.
- The UI clears the credential field after parsing and displays only a masked account identifier. The live console buffer redacts credential-like `key=value` fragments before rendering or sanitized copy.
- Temporary and keep-session browser modes use generated application-owned directories. They do not import or attach to an existing Chrome profile, and temporary cleanup is limited to the exact generated directory.
- ChatGPT is the only supported provider in this phase. Unsupported providers are visible as planning states and fail before orchestration. The production route starts at the official ChatGPT origin; mock login origins are test-only.

Console-01 tests use synthetic values and disposable browser/test roots. The feature is not consent to use a credential pasted into Codex and does not certify real ChatGPT authentication or session persistence. Any future live attempt requires independent password/TOTP rotation, local Vault update, selected-account confirmation, and explicit in-app approval.

## CONSOLE-02 managed-runtime boundary

The console resolves Chromium only through the `Microsoft.Playwright` package selected by this solution. Runtime metadata may expose package version, compatible revision, readiness, and the safe cache label, but never exposes credentials or browser session material. The managed binary cache at `%LOCALAPPDATA%\\ms-playwright` is separate from generated app-owned profile directories under `%LOCALAPPDATA%\\FlashRouter\\BrowserProfiles`.

Install/Repair is a runtime maintenance action only. Its child process receives `PLAYWRIGHT_SKIP_BROWSER_GC=1`; unrelated revisions and user profiles must not be deleted. `ChromiumSandbox = true` remains enabled, and the application does not attach to personal Chrome or HuflitRegistrationTool. Huflit concurrency is `NOT VERIFIED` unless a separate authorized run proves non-interference.

Final CONSOLE-02 evidence was credential-free: no real password, TOTP seed/code, Vault unlock, authenticated account, cookie, token, local storage, or provider API request was used. Real authentication and session persistence remain blocked until the user independently rotates exposed secrets, updates the local Vault, and approves one in-app attempt.
