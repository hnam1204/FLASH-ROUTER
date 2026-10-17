namespace FlashAI.Modules.Authenticator.Integration;

public enum TotpAlgorithm
{
    Sha1,
    Sha256,
    Sha512
}

public interface ITotpGenerator
{
    string Generate(ReadOnlySpan<byte> secret, DateTimeOffset utcNow, int digits, int period);

    string Generate(
        ReadOnlySpan<byte> secret,
        DateTimeOffset utcNow,
        int digits,
        int period,
        TotpAlgorithm algorithm);
}
