using FlashAI.Modules.ImportExport.Application;
using FlashAI.Modules.ImportExport.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.ImportExport;

public static class ImportExportModuleRegistration
{
    public static IServiceCollection AddImportExportModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<PreviewImportExportUiPort>();
        services.AddScoped<IImportExportUiPort, PreviewImportExportUiPort>();
        return services;
    }
}
