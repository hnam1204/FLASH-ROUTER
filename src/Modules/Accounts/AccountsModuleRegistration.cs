using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Accounts.Persistence;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Accounts;

public static class AccountsModuleRegistration
{
    public static IServiceCollection AddAccountsModule(this IServiceCollection services)
    {
        services.AddScoped<PreviewAccountWorkspaceService>();
        services.AddScoped<SqliteAccountWorkspaceReader>();
        services.AddScoped<SqliteAccountCommandPort>();
        services.AddScoped<SqliteAccountAuthenticationStateStore>();
        services.AddScoped<IAccountAuthenticationStateStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteAccountAuthenticationStateStore>());
        services.AddScoped<IAccountAuthenticationStateReader>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteAccountAuthenticationStateStore>());
        services.AddSingleton<IFlashAiModelContributor, AccountModelContributor>();
        services.AddScoped<IAccountWorkspaceReader>(ResolveWorkspaceReader);
        services.AddScoped<IAccountCommandPort>(ResolveCommandPort);
        services.AddScoped<IAccountCatalog>(ResolveCatalog);
        return services;
    }

    private static IAccountWorkspaceReader ResolveWorkspaceReader(IServiceProvider serviceProvider)
        => IsIntegrated(serviceProvider)
            ? serviceProvider.GetRequiredService<SqliteAccountWorkspaceReader>()
            : serviceProvider.GetRequiredService<PreviewAccountWorkspaceService>();

    private static IAccountCommandPort ResolveCommandPort(IServiceProvider serviceProvider)
        => IsIntegrated(serviceProvider)
            ? serviceProvider.GetRequiredService<SqliteAccountCommandPort>()
            : serviceProvider.GetRequiredService<PreviewAccountWorkspaceService>();

    private static IAccountCatalog ResolveCatalog(IServiceProvider serviceProvider)
        => IsIntegrated(serviceProvider)
            ? serviceProvider.GetRequiredService<SqliteAccountWorkspaceReader>()
            : serviceProvider.GetRequiredService<PreviewAccountWorkspaceService>();

    private static bool IsIntegrated(IServiceProvider serviceProvider)
        => string.Equals(
            serviceProvider.GetRequiredService<IConfiguration>()["FlashAI:AccountStorageMode"],
            "Integrated",
            StringComparison.OrdinalIgnoreCase);
}
