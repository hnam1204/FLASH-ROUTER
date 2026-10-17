namespace FlashAI.Foundation.Hosting;

public static class StartupValidator
{
    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        LocalBindingGuard.ValidateConfiguration(configuration);

        var storageMode = configuration["FlashAI:AccountStorageMode"];
        if (!string.Equals(storageMode, "Preview", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(storageMode, "Integrated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("FlashAI:AccountStorageMode must be explicitly set to Preview or Integrated.");
        }

        if (string.Equals(storageMode, "Preview", StringComparison.OrdinalIgnoreCase) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Preview account storage is available only in Development.");
        }

        if (environment.IsProduction() && configuration["AllowedHosts"] == "*")
        {
            throw new InvalidOperationException("Production hosting must declare an explicit local host allow-list.");
        }
    }
}
