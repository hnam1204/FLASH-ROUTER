using FlashAI.Modules.Diagnostics.Application;
using FlashAI.Modules.Diagnostics.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Diagnostics;

public static class DiagnosticsModuleRegistration
{
    public static IServiceCollection AddDiagnosticsModule(this IServiceCollection services)
    {
        services.AddScoped<IDiagnosticsUiPort, RuntimeDiagnosticsUiPort>();
        return services;
    }
}
