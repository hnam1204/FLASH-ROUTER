namespace FlashAI.Modules.Authenticator.Contracts;

public sealed record TotpCredentialVm(
    Guid CredentialId,
    Guid AccountId,
    string Issuer,
    string AccountLabel,
    string MaskedSecret,
    int Digits = 6,
    int PeriodSeconds = 30,
    bool IsDemo = false);

public sealed record TotpCodeVm(
    string Code,
    int RemainingSeconds,
    bool IsDemo = false);
