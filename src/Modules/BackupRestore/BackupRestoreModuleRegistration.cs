using FlashAI.Modules.BackupRestore.Application;
using FlashAI.Modules.BackupRestore.Contracts;
using FlashAI.Foundation.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.BackupRestore;

public static class BackupRestoreModuleRegistration
{
    public static IServiceCollection AddBackupRestoreModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var integrated = string.Equals(
            configuration?["FlashAI:AccountStorageMode"],
            "Integrated",
            StringComparison.OrdinalIgnoreCase);

        services.AddScoped<IBackupRestoreUiPort>(serviceProvider => integrated
            ? new SqliteBackupRestoreUiPort(
                serviceProvider.GetRequiredService<LocalDataPaths>(),
                serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                serviceProvider.GetRequiredService<IDataProtectionProvider>())
            : serviceProvider.GetRequiredService<PreviewBackupRestoreUiPort>());
        services.AddScoped<PreviewBackupRestoreUiPort>();
        return services;
    }
}
