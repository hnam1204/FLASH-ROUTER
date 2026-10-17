using FlashAI.Modules.Reminders.Application;
using FlashAI.Modules.Reminders.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Reminders;

public static class RemindersModuleRegistration
{
    public static IServiceCollection AddRemindersModule(this IServiceCollection services)
    {
        services.AddScoped<IReminderUiPort, PreviewReminderUiPort>();
        return services;
    }
}
