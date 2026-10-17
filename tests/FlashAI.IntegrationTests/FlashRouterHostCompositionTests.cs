using FLASH_ROUTER.Integration;
using FlashAI.Foundation.Composition;
using FlashAI.Modules.Accounts;
using FlashAI.Modules.Activity;
using FlashAI.Modules.Authenticator;
using FlashAI.Modules.BackupRestore;
using FlashAI.Modules.Connections;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Dashboard;
using FlashAI.Modules.Diagnostics;
using FlashAI.Modules.ImportExport;
using FlashAI.Modules.Reminders;
using FlashAI.Modules.Settings;
using FlashAI.Modules.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlashAI.IntegrationTests;

public sealed class FlashRouterHostCompositionTests
{
    [Fact]
    public async Task FullHostCompositionBuildsAndResolvesBrowserAuthenticationServices()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "flash-router-di-hotfix-" + Guid.NewGuid().ToString("N"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "FLASH_ROUTER",
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Development"
        });

        builder.Host.UseDefaultServiceProvider((_, options) =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FlashAI:AccountStorageMode"] = "Integrated",
            ["FlashAI:WorkspaceRoot"] = dataRoot,
            ["FlashAI:Persistence:MigrationsAssembly"] = "FLASH_ROUTER",
            ["FlashAI:Connections:ProviderSync:Enabled"] = "false"
        });

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services
            .AddFlashFoundation(builder.Configuration, builder.Environment)
            .AddDashboardModule()
            .AddAccountsModule()
            .AddConnectionsModule(builder.Configuration)
            .AddSubscriptionsModule()
            .AddAuthenticatorModule()
            .AddImportExportModule(builder.Configuration)
            .AddRemindersModule()
            .AddBackupRestoreModule(builder.Configuration)
            .AddActivityModule()
            .AddSettingsModule()
            .AddDiagnosticsModule();

        builder.Services.AddScoped<FlashAI.Modules.ImportExport.Contracts.IImportExportUiPort, SqliteImportExportUiPort>();

        try
        {
            await using var app = builder.Build();
            await using var scope = app.Services.CreateAsyncScope();

            var browserPort = scope.ServiceProvider.GetRequiredService<IBrowserProfileUiPort>();
            var browserAuth = scope.ServiceProvider.GetRequiredService<IBrowserAuthOrchestrator>();
            var consoleAuth = scope.ServiceProvider.GetRequiredService<IEphemeralBrowserAuthOrchestrator>();
            var providerRegistry = scope.ServiceProvider.GetRequiredService<IConsoleProviderRegistry>();

            Assert.IsType<BrowserProfileUiPort>(browserPort);
            Assert.IsType<BrowserAuthOrchestrator>(browserAuth);
            Assert.IsType<BrowserAuthOrchestrator>(consoleAuth);
            Assert.Contains(providerRegistry.List(), item => item.Id == "openai-chatgpt" && item.IsSupported);
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBrowserProfileManager>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IChromeProcessLauncher>());
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }
}
