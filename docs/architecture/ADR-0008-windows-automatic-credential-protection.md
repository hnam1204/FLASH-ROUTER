# ADR-0008: Windows automatic credential protection

Status: Accepted for V4.4.1

Date: 2026-10-18

## Decision

New browser credentials use the existing `FileSecretVault` implementation backed by ASP.NET Core Data Protection. The Data Protection key ring is persisted under the local security path and protected with Windows DPAPI using the current Windows user scope (`ProtectKeysWithDpapi(protectToLocalMachine: false)`). Vault entries are encrypted, authenticated, written atomically, and addressed by a hashed logical key.

Routine browser authentication does not consult the scoped `IVaultAccessState` unlock flag. A compatible credential is read automatically under the same authorized Windows user context. Protection metadata is stored with each SQLite reference using `AspNetCoreDataProtection-DPAPI-CurrentUser`, `WindowsCurrentUser`, and format version `1`.

## Consequences

- Credentials remain encrypted at rest and are not stored in SQLite password or TOTP columns.
- The same Windows user can retrieve compatible credentials after application restart without a second master-password prompt.
- A different Windows user, a missing DPAPI-protected key ring, corruption, or inaccessible storage fails closed and is classified separately.
- Legacy protection metadata is not silently reinterpreted. It returns `LEGACY_UNLOCK_REQUIRED` and requires an authorized migration or credential replacement.
- The model is user-bound, not universally portable: profile migration, machine changes, Windows account replacement, or key-ring loss may require recovery or replacement.
- A process already running with the same compromised Windows user privileges may still access secrets; this design is not a defense against same-user malware.

## Migration policy

Migration is opt-in and one-time. The original legacy reference and encrypted material remain preserved until authorized decryption, new-scheme protection, round-trip verification, and SQLite reference update all succeed. If the legacy secret cannot be unlocked, the application preserves it and requires credential replacement.

## Verification

The V4.4.1 focused suite covers restart round trips, wrong key rings, ciphertext integrity, SQLite protection metadata, legacy classification, and duplicate Auto Login suppression. Live provider authentication and Windows User B isolation are not certified by the current test run.
