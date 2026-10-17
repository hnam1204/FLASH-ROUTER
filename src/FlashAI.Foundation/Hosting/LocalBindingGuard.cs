namespace FlashAI.Foundation.Hosting;

public static class LocalBindingGuard
{
    public static void ValidateConfiguration(IConfiguration configuration)
    {
        var configuredUrls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"];
        if (string.IsNullOrWhiteSpace(configuredUrls))
        {
            return;
        }

        var invalidUrl = configuredUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(url => !IsLocalUrl(url));

        if (invalidUrl is not null)
        {
            throw new InvalidOperationException($"FLASH ROUTER only permits localhost bindings. Invalid URL: {invalidUrl}");
        }
    }

    private static bool IsLocalUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}
