# BE-02 — Credential Vault, Secure Import & Local TOTP

Ngày kiểm tra: 2026-10-15  
Workspace: `D:\ĐỒ ÁN WEBSITE\FLASH ROUTER\FLASH ROUTER`  
Solution: `FLASH ROUTER.sln`  
Target framework: `net8.0`

## A. Executive Summary

- `BE-00 — GO` và `BE-01 — GO` đã được chứng minh trước phase này.
- `ISecretVault` hiện được composition sang `FileSecretVault` trong Integrated mode; Preview mode tiếp tục dùng Authenticator in-memory demo.
- Secret values được bảo vệ bằng ASP.NET Core Data Protection với key ring Windows DPAPI user scope hiện hữu. Ciphertext runtime nằm riêng trong `.local/security/vault`; key ring XML cũ không bị xóa hoặc thay thế.
- Authenticator đã có passphrase verifier PBKDF2, lock/unlock theo circuit, credential ownership theo `AccountId`, secret version/revision, rotation qua overwrite, masked metadata index và RFC 6238 TOTP local.
- Legacy `email|password|totp` được xử lý bằng session memory-only: preview đã mask, separator không hợp lệ bị từ chối, explicit commit mới ghi từng credential value vào vault và không tạo plaintext staging file.

## B. Architecture Changes

| Boundary | Implementation | Storage | Status |
| --- | --- | --- | --- |
| `ISecretVault` | `FileSecretVault` | Authenticated encrypted files with hashed key names | Implemented |
| Data Protection | Existing `DataProtectionSetup` | `.local/security` DPAPI user-protected key ring | Preserved |
| Authenticator port | `DpapiAuthenticatorUiPort` | Encrypted TOTP payload + encrypted metadata index | Implemented |
| TOTP | `Rfc6238TotpGenerator` | No OTP persistence | Implemented |
| Credential import | `SecureCredentialImportParser` / session | Memory-only parse, encrypted explicit commit | Implemented |

The vault binds the logical key into the Data Protection purpose chain, so copying ciphertext from one logical entry to another fails authentication. File writes use a same-directory temporary file and replacement. The envelope carries scheme version, revision, creation time and update time.

## C. Security Evidence

- No real credentials, provider calls, OAuth, cookies, browser password stores, MFA or CAPTCHA automation were used.
- Synthetic ciphertext inspection confirms test secrets are not present in vault files.
- Wrong key-ring access fails with safe `SECRET_VAULT_UNAVAILABLE` handling; no secret value is included in the error message.
- TOTP values are decoded only in memory, generated locally, and cleared after use. Short-lived OTP codes are not stored in the vault.
- ViewModels expose only masked secret text. The Authenticator UI no longer places the current OTP value in a toast.
- Import preview exposes masked values and errors do not include raw passwords or TOTP values. Passwords containing `|` are rejected rather than truncated.
- Account and provider ownership are carried in encrypted credential envelopes; Authenticator queries filter by `AccountId`.
- Vault backup/restore was tested by restoring encrypted ciphertext with the same key ring. A portable backup UI is not added in BE-02.
- `SensitiveDataRedactor` coverage confirms password/secret keys are redacted.
- Existing `.local/security/key-473e4b2c-e250-46a3-923e-89c7f836a5c9.xml` remains preserved. No SQLite migration or existing account data mutation was performed.

Secure deletion is a best-effort ciphertext-file removal. Forensic erasure on SSD/filesystems is not claimed; recovery requires the same Windows user DPAPI key ring and the application passphrase.

## D. Tests

Command:

```powershell
dotnet test ".\FLASH ROUTER.sln" --no-restore
```

Results:

- Architecture: `8 passed, 0 failed`.
- Integration: `41 passed, 0 failed`.
- BE-02 focused security tests: `12 passed, 0 failed`.
- RFC 6238 official SHA-1 vectors: `5/5` pass.
- Build: `0` errors. Existing analyzer warnings remain in the baseline solution; no warning-suppression or framework upgrade was introduced.

Coverage includes encrypted round-trip, rotation, logical-key binding, wrong key-ring failure, encrypted backup/restore, account isolation, masked import, invalid import, duplicate detection, redaction, lock/unlock persistence, removal and no-secret TOTP ViewModels.

## E. Files

Added:

- `src/FlashAI.Foundation/Security/FileSecretVault.cs`
- `src/FlashAI.Foundation/Security/SecretVaultException.cs`
- `src/Modules/Authenticator/Application/Base32SecretDecoder.cs`
- `src/Modules/Authenticator/Application/DpapiAuthenticatorUiPort.cs`
- `src/Modules/Authenticator/Application/Rfc6238TotpGenerator.cs`
- `src/Modules/Authenticator/Features/CredentialImport/Application/SecureCredentialImportParser.cs`
- `tests/FlashAI.IntegrationTests/Be02SecurityTests.cs`

Modified:

- `src/FlashAI.Foundation/Composition/FlashFoundationRegistration.cs`
- `src/Modules/Authenticator/AuthenticatorModuleRegistration.cs`
- `src/Modules/Authenticator/Application/AuthenticatorBoundary.cs`
- `src/Modules/Authenticator/Features/TotpVault/UI/AuthenticatorTab.razor`
- `src/Modules/Authenticator/UI/AuthenticatorPage.razor`
- `src/Modules/Authenticator/README.md`

Preserved:

- `.local/security/key-473e4b2c-e250-46a3-923e-89c7f836a5c9.xml`
- `net8.0`, existing SQLite migrations/database, Feature-First module boundaries and Preview composition.

## F. Scope Status

- Designed: provider-specific credential references beyond local TOTP, portable backup UI and future OAuth token kinds.
- Implemented: local encrypted vault, TOTP credentials, secure import session and offline tests.
- Offline tested: all BE-02 security cases listed above.
- Browser verified: BE-01R matrix remains verified; BE-02 has no live provider/browser authentication scenario.
- Live authorized verified: not performed and not required for this phase.
- Production ready: not claimed. Live OAuth, provider adapters, browser-assisted sign-in and recovery UX remain later phases.

## G. Final Verdict

`BE-00 — GO`  
`BE-01 — GO`  
`BE-02 — GO (offline/local security gate)`

BE-02 is complete for the local encrypted-vault, secure-import and TOTP scope. It does not authorize or claim any live provider connection, account login, credential validity, subscription status or production deployment.
