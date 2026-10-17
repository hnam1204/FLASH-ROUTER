namespace FlashAI.Foundation.Security;

public interface ILocalTotpCodeGenerator
{
    string Generate(ReadOnlySpan<byte> secret, DateTimeOffset utcNow, int digits, int period);
}
