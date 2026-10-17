using FLASH_ROUTER.Components;
using FLASH_ROUTER.Integration;
using FlashAI.Foundation.Composition;
using FlashAI.Foundation.Hosting;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts;
using FlashAI.Modules.Activity;
using FlashAI.Modules.Authenticator;
using FlashAI.Modules.BackupRestore;
using FlashAI.Modules.Connections;
using FlashAI.Modules.Dashboard;
using FlashAI.Modules.Diagnostics;
using FlashAI.Modules.ImportExport;
using FlashAI.Modules.Reminders;
using FlashAI.Modules.Settings;
using FlashAI.Modules.Subscriptions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();
StartupValidator.Validate(builder.Configuration, builder.Environment);

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

if (string.Equals(builder.Configuration["FlashAI:AccountStorageMode"], "Integrated", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<FlashAI.Modules.ImportExport.Contracts.IImportExportUiPort, SqliteImportExportUiPort>();
}

var app = builder.Build();

var configuredUrls = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["urls"] ?? string.Empty;
var hasHttpsEndpoint = configuredUrls
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Any(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));

if (string.Equals(builder.Configuration["FlashAI:AccountStorageMode"], "Integrated", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<MigrationCoordinator>().MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!app.Environment.IsDevelopment() && hasHttpsEndpoint)
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseFlashSecurityHeaders();
app.UseAntiforgery();

app.MapGet(ManagedHostControl.ReadyPath, () => Results.Ok(new
{
    status = "ready",
    version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown"
}));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(ModuleAssemblyCatalog.All.ToArray());

app.Run();
