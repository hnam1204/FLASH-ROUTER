using FlashAI.Modules.Activity.Application;
using FlashAI.Modules.Activity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Activity;

public static class ActivityModuleRegistration
{
    public static IServiceCollection AddActivityModule(this IServiceCollection services)
    {
        services.AddScoped<IActivityUiPort, PreviewActivityUiPort>();
        return services;
    }
}
