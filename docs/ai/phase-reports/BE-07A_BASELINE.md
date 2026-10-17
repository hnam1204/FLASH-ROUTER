# FLASH ROUTER - BE-07A Baseline Certification

Date: 2026-10-16
Workspace: `D:\\DO AN WEBSITE\\FLASH ROUTER\\FLASH ROUTER`

## Scope and evidence

This baseline was collected from the existing solution, the BE-06 reports, the current SQLite metadata, the current provider source, and fresh Release build/test runs. No Git repository is present at the workspace root, so branch, HEAD, commit, and diff evidence are unavailable. Existing files and protected local data were not reset or replaced.

## Inventory

| Item | Evidence |
| --- | --- |
| Solution projects | 15 projects in `FLASH ROUTER.sln` |
| Feature modules | 11 registered modules |
| Feature directories | 51 source feature directories in the BE-06 inventory |
| Provider adapters | 6 API adapters: OpenRouter, OpenAI Platform, Anthropic, Gemini, DeepSeek, Groq |
| EF migrations | 6 host migrations |
| Target framework | `net8.0` for all projects |
| SDKs | 8.0.424, 8.0.426, 9.0.306 |
| .NET runtimes | ASP.NET Core/.NET 8 and 9; no .NET 10 runtime or SDK |
| MSBuild | 17.14.28 from SDK 9.0.306 |
| `global.json` | Not present |
| Data store | One host-owned SQLite database |
| Vault | Windows-user Data Protection/DPAPI key ring and encrypted file vault |
| Binding policy | Localhost/loopback only |

## Build and test baseline

Fresh commands completed successfully:

- `dotnet build .\\FLASH ROUTER.sln --configuration Release --no-restore`: 0 warnings, 0 errors.
- `dotnet test .\\FLASH ROUTER.sln --configuration Release --no-restore`: architecture 8 passed, integration 75 passed, 0 failed, 0 skipped.
- The integration count includes two BE-07 regression tests covering failed provider checks and latest-snapshot retention in both in-memory and SQLite stores.

The existing BE-06 security, OAuth, backup/recovery, provider fixture, and browser evidence remains offline evidence only. No provider credential, OAuth account, billable request, or browser session was used.

## SQLite read-only check

The active database was opened read-only using the same connection policy as the application (`Foreign Keys=True`, bounded timeout). Only safe metadata and counts were read.

| Check | Result |
| --- | --- |
| Database | `.local/data/flashrouter.db` |
| `integrity_check` | `ok` |
| Foreign keys | `1` |
| Journal mode | `wal` |
| Migration rows | 6 |
| Accounts | 0 |
| Provider connections | 0 |
| Provider observations | 0 |
| Provider sync states | 0 |
| Provider check runs | 0 |

The standalone SQLite foreign-key result is not used as application evidence; the application-scoped check above is the relevant result.

## Release and security baseline

The prior BE-06 artifact remains preserved and its 388 checksums were reverified. A new current-source BE-07 artifact is documented in `BE-07F_WINDOWS_RELEASE.md` and `.local/releases/BE-07/`.

The NuGet audit still reports these transitive vulnerable packages:

- `System.Security.Cryptography.Pkcs` 8.0.1: High and Moderate advisories.
- `System.Security.Cryptography.Xml` 8.0.4: High advisories.

This is an open security hardening finding. It does not indicate SQLite corruption, vault plaintext exposure, or a failed offline test, but it prevents a security-clean release claim.

## Verdict

`BE-07A BASELINE - GO`

The solution is buildable and its current local database is intact. The GO is limited to the offline/local scope; it does not clear the package advisories, live provider verification, or .NET 10 migration gates.
