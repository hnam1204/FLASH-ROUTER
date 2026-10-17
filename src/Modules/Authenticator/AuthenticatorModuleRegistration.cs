using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Authenticator.Contracts;
using FlashAI.Modules.Authenticator.Features.CredentialImport.Application;
using FlashAI.Modules.Authenticator.Integration;
using FlashAI.Foundation.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Authenticator;

public static class AuthenticatorModuleRegistration
{
    public static IServiceCollection AddAuthenticatorModule(this IServiceCollection services)
    {
        services.AddSingleton<Rfc6238TotpGenerator>();
        services.AddSingleton<ITotpGenerator>(serviceProvider =>
            serviceProvider.GetRequiredService<Rfc6238TotpGenerator>());
        services.AddSingleton<ILocalTotpCodeGenerator>(serviceProvider =>
            serviceProvider.GetRequiredService<Rfc6238TotpGenerator>());
        services.AddSingleton<SecureCredentialImportParser>();
        services.AddScoped<IAuthenticatorUiPort>(serviceProvider =>
            IsIntegrated(serviceProvider)
                ? new DpapiAuthenticatorUiPort(
                    serviceProvider.GetRequiredService<ISecretVault>(),
                    serviceProvider.GetRequiredService<ITotpGenerator>(),
                    serviceProvider.GetRequiredService<IVaultAccessState>())
                : serviceProvider.GetRequiredService<PreviewAuthenticatorUiPort>());
        services.AddScoped<PreviewAuthenticatorUiPort>();
        return services;
    }

    private static bool IsIntegrated(IServiceProvider serviceProvider)
        => string.Equals(
            serviceProvider.GetRequiredService<IConfiguration>()["FlashAI:AccountStorageMode"],
            "Integrated",
            StringComparison.OrdinalIgnoreCase);
}
