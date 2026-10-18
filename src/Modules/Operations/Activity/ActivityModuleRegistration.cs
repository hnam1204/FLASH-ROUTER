using FlashAI.Modules.Activity.Application;
using FlashAI.Modules.Activity.Contracts;
using FlashAI.Foundation.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Activity;

public static class ActivityModuleRegistration
{
    public static IServiceCollection AddActivityModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var integrated = string.Equals(
            configuration?["FlashAI:AccountStorageMode"],
            "Integrated",
            StringComparison.OrdinalIgnoreCase);
        services.AddScoped<PreviewActivityUiPort>();
        services.AddScoped<SqliteActivityUiPort>();
        services.AddSingleton<IFlashAiModelContributor, Persistence.ActivityModelContributor>();
        services.AddSingleton<InMemoryAuditSink>();
        services.AddSingleton<InProcessActivityEventStream>();
        services.AddSingleton<IActivityEventStream>(serviceProvider =>
            serviceProvider.GetRequiredService<InProcessActivityEventStream>());
        services.AddScoped<SqliteAuditSink>();
        services.AddScoped<IActivityUiPort>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<SqliteActivityUiPort>()
            : serviceProvider.GetRequiredService<PreviewActivityUiPort>());
        services.AddScoped<IAuditSink>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<SqliteAuditSink>()
            : serviceProvider.GetRequiredService<InMemoryAuditSink>());
        services.AddScoped<FlashAI.Foundation.Contracts.IAuditSink>(serviceProvider =>
            serviceProvider.GetRequiredService<IAuditSink>());
        return services;
    }
}
