using FlashAI.Modules.Settings.Application;
using FlashAI.Modules.Settings.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Settings;

public static class SettingsModuleRegistration
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<ISettingsUiPort, PreviewSettingsUiPort>();
        return services;
    }
}
