namespace FlashAI.Foundation.Observability;

public sealed class SensitiveDataRedactor
{
    private static readonly string[] SensitiveKeys = ["password", "token", "secret", "seed", "authorization", "code"];

    public string Redact(string key, string? value)
    {
        if (SensitiveKeys.Any(sensitive => key.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
        {
            return "[REDACTED]";
        }

        return value ?? string.Empty;
    }
}
