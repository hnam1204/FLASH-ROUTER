# ADR-0004: Secret storage boundary

Status: Implemented for BE-02

The foundation exposes `ISecretVault` and registers `FileSecretVault` for Integrated mode. Values are protected with ASP.NET Core Data Protection whose existing Windows DPAPI user-scoped key ring remains at `.local/security`; encrypted entry files are isolated under `.local/security/vault` and use hashed logical key names plus a key-bound purpose chain.

Authenticator owns encrypted TOTP payloads and masked metadata, while the legacy credential import session keeps raw values in memory only until explicit encrypted commit. The vault envelope carries scheme version and revision metadata, writes atomically, and fails closed on key-ring mismatch or corruption. BE-02 does not add live provider authentication or a portable backup UI.
