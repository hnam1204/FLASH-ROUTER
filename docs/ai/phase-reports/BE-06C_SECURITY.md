# BE-06C — Security Hardening

**Ngày:** 2026-10-16  
**Kết luận:** `BE-06C SECURITY — NO-GO FOR SECURITY-CLEAN RELEASE; CONDITIONAL OFFLINE CANDIDATE`

## Controls verified

- Local binding guard accepts loopback URLs and rejects wildcard binding; final release listener was `127.0.0.1:5281`.
- Allowed hosts are restricted to local host values. State-changing paths remain behind the server-side component/request boundaries; the app does not expose a public listener.
- DPAPI/Data Protection vault tests cover ciphertext confidentiality, logical-key binding, wrong-key-ring rejection, corruption rejection, atomic concurrent writes, reparse-point rejection, authenticator lock/disposal and redacted logging.
- Provider HTTP execution uses fixed official HTTPS host allowlists, bounded response handling, disabled redirects, cancellation/timeouts and safe error mapping. Credentials are not forwarded to arbitrary URLs.
- Import parsing is memory-first with masked preview, duplicate/separator validation and explicit encrypted commit; no bulk password/MFA login is performed.
- Diagnostics and observations exclude passwords, tokens, TOTP seeds, cookies, authorization headers and raw provider responses.

## Dependency scan

`dotnet list FLASH ROUTER.sln package --vulnerable --include-transitive` reached NuGet and reported:

| Package | Resolved | Severity reported |
| --- | --- | --- |
| `System.Security.Cryptography.Pkcs` | `8.0.1` | High and Moderate advisories |
| `System.Security.Cryptography.Xml` | `8.0.4` | Multiple High advisories |

The advisories are not silently suppressed or reclassified. No package upgrade was made during BE-06 because version changes require a separate compatibility/security review.

## Gate

The application security boundaries and synthetic leakage tests pass, but the open High dependency advisories prevent a security-clean release claim. Remediation or an explicit risk acceptance is required before production/security certification. Live OAuth/provider verification is `NOT EXECUTED / NOT VERIFIED`.
