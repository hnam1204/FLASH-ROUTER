# FLASH ROUTER - BE-07D .NET 10 LTS Readiness

Date: 2026-10-16

## Toolchain boundary

Installed SDKs are `8.0.424`, `8.0.426`, and `9.0.306`. No .NET 10 SDK, .NET 10 runtime, `global.json`, or Visual Studio compatibility evidence for .NET 10 was found. The source remains on `net8.0`; no system SDK was installed and no framework migration was attempted.

## Dependency inventory

| Area | Current reference | .NET 10 readiness action | Risk |
| --- | --- | --- | --- |
| ASP.NET Core/Blazor host | Framework reference from `Microsoft.NET.Sdk.Web`, `net8.0` | Validate on ASP.NET Core 10 with a supported SDK; keep render modes and routes unchanged | Medium |
| EF Core and SQLite | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` 8.0.30 | Move as one EF Core major-version set after compatibility review; run migrations only on disposable copies first | High |
| Data Protection | `Microsoft.AspNetCore.DataProtection.Extensions` 8.0.30 plus framework packages | Verify existing key-ring readability and vault decryptability under net10; never regenerate keys | High |
| OAuth/OIDC contracts | Framework HTTP/crypto and application code; no separate OAuth SDK | Re-run PKCE, state, nonce, issuer, audience, expiration, replay, and token-vault tests | High |
| Provider HTTP | `HttpClientFactory`, fixed `win-x64` hosts, 20-second timeout, redirects disabled | Re-run host allowlist, safe error, retry, cancellation, and response-bound tests | Medium |
| Background services | `BackgroundService`/`PeriodicTimer` | Re-run disabled-by-default scheduler and graceful shutdown tests | Medium |
| Tests | Microsoft.NET.Test.Sdk 17.11.1, xUnit 2.9.2, runner 2.8.2 | Upgrade only as part of the approved migration test matrix | Low/Medium |
| Native SQLite | `SQLitePCLRaw` 2.1.12 transitively | Validate native `win-x64` loading and migration behavior after EF/SQLite upgrade | High |
| Language | `LangVersion=latest` | Verify compiler behavior for the selected SDK; C# 14 is not certified here | Medium |

NuGet metadata shows available 10.0.13 lines for several framework and EF packages, but that is an update report, not a compatibility approval. The transitive High advisories for `System.Security.Cryptography.Pkcs` 8.0.1 and `System.Security.Cryptography.Xml` 8.0.4 remain open.

## Compatibility findings

- Feature-first module boundaries, typed UI ports, the single SQLite migration assembly, Data Protection vault, local binding guard, and provider host allowlist are isolated enough for a staged framework migration.
- EF model mappings and six additive migrations must be tested against a copied database. Existing migrations must not be regenerated or removed.
- SQLite `DateTimeOffset` ordering is provider-sensitive. BE-07 fixed provider observation ordering by materializing run metadata before UTC-aware ordering; this behavior must be retained and retested after EF Core 10.
- Blazor Interactive Server, antiforgery, CSS isolation, SignalR circuit behavior, and browser regression have not been compiled or executed under net10.

## Migration and rollback sequence

`net8 baseline and backup` -> `install approved net10 SDK/IDE` -> `upgrade package set` -> `change targets` -> `build/analyzers` -> `architecture/integration/security tests` -> `disposable SQLite migration` -> `vault decrypt/read test without key replacement` -> `OAuth offline tests` -> `browser matrix` -> `win-x64 publish` -> `isolated cold start` -> `release checksums`.

Rollback is a source/package rollback plus restore of a validated encrypted backup into a separate test path. SQLite does not provide a general automatic down-migration guarantee. The active user database and DPAPI key ring must not be overwritten by a migration experiment.

## Verdicts

`BE-07D NET10 READINESS - GO (UPGRADE READY, TOOLING GATED)`

`BE-07D NET10 MIGRATION - NOT AUTHORIZED / NOT VERIFIED`

The solution is ready for a separately approved migration work item, but the machine and approval gates do not support claiming a migrated or net10-compiled result.
