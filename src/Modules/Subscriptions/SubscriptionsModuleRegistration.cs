using FlashAI.Modules.Subscriptions.Application;
using FlashAI.Modules.Subscriptions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Subscriptions;

public static class SubscriptionsModuleRegistration
{
    public static IServiceCollection AddSubscriptionsModule(this IServiceCollection services)
    {
        services.AddScoped<ISubscriptionUiPort, PreviewSubscriptionUiPort>();
        return services;
    }
}
