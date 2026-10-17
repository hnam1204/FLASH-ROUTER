using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Hosting;
using FlashAI.Foundation.Observability;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Foundation.Composition;

public static class FlashFoundationRegistration
{
    public static IServiceCollection AddFlashFoundation(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var workspaceRoot = configuration["FlashAI:WorkspaceRoot"]
            ?? Directory.GetParent(environment.ContentRootPath)?.FullName
            ?? environment.ContentRootPath;
        var automationRoot = configuration["FlashAI:AutomationRoot"];
        var paths = new LocalDataPaths(workspaceRoot, automationRoot);

        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<LocalAccessPolicy>();
        services.AddSingleton<SensitiveDataRedactor>();
        services.AddScoped<IVaultAccessState, VaultAccessState>();
        services.AddSingleton<MigrationCoordinator>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<FlashAiDbContextFactory>();
        services.AddHostedService<ManagedHostControlHostedService>();
        var migrationsAssembly = configuration["FlashAI:Persistence:MigrationsAssembly"] ?? "FLASH_ROUTER";
        services.AddDbContextFactory<FlashAiDbContext>((serviceProvider, options) =>
        {
            var connectionFactory = serviceProvider.GetRequiredService<SqliteConnectionFactory>();
            options.UseSqlite(
                connectionFactory.CreateConnectionString(),
                sqlite => sqlite.MigrationsAssembly(migrationsAssembly));
        });
        services.AddFlashDataProtection(paths.Security);
        services.AddSingleton<ISecretVault>(serviceProvider =>
            new FileSecretVault(
                Path.Combine(paths.Security, "vault"),
                serviceProvider.GetRequiredService<IDataProtectionProvider>()));
        return services;
    }
}

public sealed class FlashAiDbContextFactory
{
    private readonly SqliteConnectionFactory _connections;

    public FlashAiDbContextFactory(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public string ConnectionString => _connections.CreateConnectionString();
}

