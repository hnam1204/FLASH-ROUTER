namespace FlashAI.Modules.Authenticator.Domain;

public sealed record TotpCredentialRecord(
    Guid Id,
    string Label,
    string Issuer,
    string Algorithm,
    int Digits,
    int Period);
