using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using FlashAI.Modules.Connections.Features.OAuthCallback.Security;
using FlashAI.Modules.Connections.Features.RefreshConnection.Application;
using FlashAI.Modules.Connections.Integration;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Persistence;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Application;
using FlashAI.Modules.Connections.Features.ProviderIntegration.CredentialResolution;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Anthropic;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.DeepSeek;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Gemini;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Groq;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.OpenAIPlatform;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.OpenRouter;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Modules.Connections;

public static class ConnectionsModuleRegistration
{
    public static IServiceCollection AddConnectionsModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var integrated = string.Equals(
            configuration?["FlashAI:AccountStorageMode"],
            "Integrated",
            StringComparison.OrdinalIgnoreCase);
        var eligibilityConfirmed = bool.TryParse(
            configuration?["FlashAI:Connections:OfficialOAuth:EligibilityConfirmed"],
            out var configuredEligibility)
            && configuredEligibility;
        var options = new OpenAiOAuthOptions(
            configuration?["FlashAI:Connections:OfficialOAuth:AgentName"] ?? "FLASH ROUTER",
            eligibilityConfirmed,
            TimeSpan.FromMinutes(5),
            1455);

        services.AddSingleton(options);
        services.AddSingleton<IOfficialOAuthEligibility, OfficialOAuthEligibility>();
        services.AddHttpClient<OpenAiHttpOAuthTokenClient>();
        services.AddHttpClient<OpenAiHttpJwksResolver>();
        AddProviderHttpClients(services);
        services.AddSingleton<IOAuthTokenClient>(serviceProvider =>
            serviceProvider.GetRequiredService<OpenAiHttpOAuthTokenClient>());
        services.AddSingleton<IOpenAiJwksResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<OpenAiHttpJwksResolver>());
        services.AddSingleton<OpenAiHostIdentifierProvider>();
        services.AddSingleton<OAuthTokenVaultStore>();
        services.AddSingleton<IOAuthConnectionStore>(serviceProvider => integrated
            ? new SqliteOAuthConnectionStore(serviceProvider.GetRequiredService<IDbContextFactory<FlashAiDbContext>>())
            : new InMemoryOAuthConnectionStore());
        services.AddSingleton<ILoopbackCallbackEndpointFactory>(
            new FixedLoopbackCallbackEndpointFactory(options.CallbackPort));
        services.AddSingleton<OpenAiIdTokenValidator>();
        services.AddSingleton<OpenAiOAuthAuthorizationService>();
        services.AddSingleton<IOfficialAuthorizationPort, OfficialAuthorizationPort>();
        services.AddSingleton<OAuthConnectionSessionService>();
        services.AddScoped<DurableConnectionUiPort>();
        services.AddSingleton<IChromeExecutableResolver, ChromeExecutableResolver>();
        services.AddSingleton<IPlaywrightBrowserRuntime, PlaywrightBrowserRuntime>();
        services.AddSingleton<IChromeProfileLockInspector, ChromeProfileLockInspector>();
        services.AddSingleton<IBrowserProfileOwnershipCoordinator, BrowserProfileOwnershipCoordinator>();
        services.AddSingleton<IChromeProcessLauncher, ChromeProcessLauncher>();
        services.AddSingleton<IAccountBrowserSessionManager, AccountBrowserSessionManager>();
        services.AddSingleton<IAccountBrowserSessionRegistration>(serviceProvider =>
            serviceProvider.GetRequiredService<IAccountBrowserSessionManager>() as IAccountBrowserSessionRegistration
            ?? throw new InvalidOperationException("The account browser manager must support session registration."));
        services.AddSingleton<BrowserProfileManager>();
        services.AddSingleton<IBrowserProfileManager>(serviceProvider =>
            serviceProvider.GetRequiredService<BrowserProfileManager>());
        services.AddSingleton<IBrowserProfilePromotionService>(serviceProvider =>
            serviceProvider.GetRequiredService<BrowserProfileManager>());
        services.AddSingleton<IEphemeralBrowserProfileFactory, EphemeralBrowserProfileFactory>();
        services.AddSingleton<IConsoleProviderRegistry, ConsoleProviderRegistry>();
        services.AddSingleton<IBrowserAuthenticationSessionFactory, PlaywrightBrowserAuthenticationSessionFactory>();
        services.AddSingleton<IBrowserPageStateDetector, StableBrowserPageStateDetector>();
        services.AddScoped<BrowserAuthCredentialReader>();
        services.AddScoped<ICleanBrowserReloginService, CleanBrowserReloginService>();
        services.AddScoped<BrowserAuthOrchestrator>();
        services.AddScoped<PreviewBrowserAuthOrchestrator>();
        services.AddScoped<IBrowserAuthOrchestrator>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<BrowserAuthOrchestrator>()
            : serviceProvider.GetRequiredService<PreviewBrowserAuthOrchestrator>());
        services.AddScoped<IEphemeralBrowserAuthOrchestrator>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<BrowserAuthOrchestrator>()
            : serviceProvider.GetRequiredService<PreviewBrowserAuthOrchestrator>());
        // Keep the legacy public constructor for existing callers, but make the
        // AUTH-BROWSER-01 constructor explicit for the DI graph.
        services.AddScoped<BrowserProfileUiPort>(serviceProvider =>
            new BrowserProfileUiPort(
                serviceProvider.GetRequiredService<IBrowserProfileManager>(),
                serviceProvider.GetRequiredService<IChromeProcessLauncher>()));
        services.AddScoped<PreviewBrowserProfileUiPort>();
        services.AddScoped<IBrowserProfileUiPort>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<BrowserProfileUiPort>()
            : serviceProvider.GetRequiredService<PreviewBrowserProfileUiPort>());
        services.AddScoped<SqliteAccountCredentialUiPort>();
        services.AddScoped<PreviewAccountCredentialUiPort>();
        services.AddScoped<AccountCopyUiPort>();
        services.AddScoped<PreviewAccountCopyUiPort>();
        services.AddScoped<IAccountCopyUiPort>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<AccountCopyUiPort>()
            : serviceProvider.GetRequiredService<PreviewAccountCopyUiPort>());
        services.AddScoped<OfficialLoginUiPort>();
        services.AddScoped<IOfficialLoginUiPort>(serviceProvider =>
            serviceProvider.GetRequiredService<OfficialLoginUiPort>());
        services.AddScoped<IAccountCredentialUiPort>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<SqliteAccountCredentialUiPort>()
            : serviceProvider.GetRequiredService<PreviewAccountCredentialUiPort>());
        services.AddScoped<IProviderUiPort, ProviderUiPort>();
        services.AddScoped<IConnectionUiPort>(serviceProvider => integrated
            ? serviceProvider.GetRequiredService<DurableConnectionUiPort>()
            : new PreviewConnectionUiPort());
        services.AddSingleton<ProviderHttpExecutor>();
        services.AddSingleton<IProviderErrorMapper, ProviderErrorMapper>();
        services.AddSingleton<IProviderCapabilityResolver, DefaultProviderCapabilityResolver>();
        services.AddSingleton<IProviderCredentialResolver, ProviderCredentialVaultStore>();
        services.AddSingleton<IAiProviderAdapter, OpenRouterAdapter>();
        services.AddSingleton<IAiProviderAdapter, OpenAiPlatformAdapter>();
        services.AddSingleton<IAiProviderAdapter, AnthropicAdapter>();
        services.AddSingleton<IAiProviderAdapter, GeminiAdapter>();
        services.AddSingleton<IAiProviderAdapter, DeepSeekAdapter>();
        services.AddSingleton<IAiProviderAdapter, GroqAdapter>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IProviderConnectionCatalog>(serviceProvider => integrated
            ? new SqliteProviderConnectionCatalog(
                serviceProvider.GetRequiredService<IDbContextFactory<FlashAiDbContext>>(),
                serviceProvider.GetRequiredService<IProviderCredentialResolver>(),
                serviceProvider.GetRequiredService<IProviderRegistry>())
            : new InMemoryProviderConnectionCatalog(
                serviceProvider.GetRequiredService<IProviderCredentialResolver>(),
                serviceProvider.GetRequiredService<IProviderRegistry>()));
        services.AddSingleton<IAccountConnectionsReader>(serviceProvider => integrated
            ? new SqliteAccountConnectionsReader(
                serviceProvider.GetRequiredService<IDbContextFactory<FlashAiDbContext>>(),
                serviceProvider.GetRequiredService<IProviderRegistry>())
            : new InMemoryAccountConnectionsReader(
                serviceProvider.GetRequiredService<IProviderConnectionCatalog>(),
                serviceProvider.GetRequiredService<IOAuthConnectionStore>(),
                serviceProvider.GetRequiredService<IProviderRegistry>()));
        services.AddSingleton<IProviderObservationWriter>(serviceProvider => integrated
            ? new SqliteProviderObservationStore(serviceProvider.GetRequiredService<IDbContextFactory<FlashAiDbContext>>())
            : new InMemoryProviderObservationStore());
        services.AddSingleton<IProviderObservationReader>(serviceProvider =>
            (IProviderObservationReader)serviceProvider.GetRequiredService<IProviderObservationWriter>());
        services.AddSingleton<IConnectionObservationsReader, ConnectionObservationsReader>();
        services.AddSingleton<IAccountUsageReader, AccountUsageReader>();
        services.AddSingleton<IProviderConnectionVerifier, ProviderConnectionVerifier>();
        services.AddSingleton<ProviderRateLimiter>();
        services.AddSingleton<IProviderSyncOrchestrator, ProviderSyncOrchestrator>();
        services.AddSingleton<IProviderSyncStateStore, SqliteProviderSyncStateStore>();
        services.AddSingleton(new ProviderSyncOptions
        {
            Enabled = !bool.TryParse(configuration?["FlashAI:Connections:ProviderSync:Enabled"], out var syncEnabled) || syncEnabled,
            Interval = TimeSpan.FromSeconds(ParsePositiveInt(configuration?["FlashAI:Connections:ProviderSync:IntervalSeconds"], 60)),
            AllowOfficialUsageReads = bool.TryParse(configuration?["FlashAI:Connections:ProviderSync:AllowOfficialUsageReads"], out var allowUsage) && allowUsage
        });
        services.AddHostedService<ProviderSyncScheduler>();
        services.AddSingleton<IFlashAiModelContributor, ConnectionModelContributor>();
        return services;
    }

    private static void AddProviderHttpClients(IServiceCollection services)
    {
        AddProviderClient(services, "provider.openrouter", "https://openrouter.ai/");
        AddProviderClient(services, "provider.openai-platform", "https://api.openai.com/");
        AddProviderClient(services, "provider.anthropic", "https://api.anthropic.com/");
        AddProviderClient(services, "provider.gemini", "https://generativelanguage.googleapis.com/");
        AddProviderClient(services, "provider.deepseek", "https://api.deepseek.com/");
        AddProviderClient(services, "provider.groq", "https://api.groq.com/");
    }

    private static void AddProviderClient(IServiceCollection services, string name, string baseAddress)
    {
        services.AddHttpClient(name, client =>
            {
                client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
    }

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
