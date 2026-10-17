using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Diagnostics.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FlashAI.Modules.Diagnostics.Application;

public sealed class RuntimeDiagnosticsUiPort : IDiagnosticsUiPort
{
    private static readonly string[] ModuleNames =
    [
        "Accounts", "Connections", "Subscriptions", "Authenticator", "ImportExport",
        "Reminders", "BackupRestore", "Activity", "Settings", "Diagnostics", "Dashboard"
    ];

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IServer _server;
    private readonly LocalDataPaths _paths;
    private readonly ISecretVault _vault;
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly IEnumerable<IFlashAiModelContributor> _contributors;

    public RuntimeDiagnosticsUiPort(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IServer server,
        LocalDataPaths paths,
        ISecretVault vault,
        IDbContextFactory<FlashAiDbContext> contextFactory,
        IEnumerable<IFlashAiModelContributor> contributors)
    {
        _configuration = configuration;
        _environment = environment;
        _server = server;
        _paths = paths;
        _vault = vault;
        _contextFactory = contextFactory;
        _contributors = contributors;
    }

    public async Task<DiagnosticsVm> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var integrated = string.Equals(
            _configuration["FlashAI:AccountStorageMode"],
            "Integrated",
            StringComparison.OrdinalIgnoreCase);
        var sqliteStatus = await ReadSqliteStatusAsync(integrated, cancellationToken);
        var vaultAvailable = await _vault.IsAvailableAsync(cancellationToken);
        var binding = ReadBinding();
        var registeredModules = _contributors.Count();
        var moduleState = integrated ? "Loaded / Integrated" : "Loaded / Preview";

        return new DiagnosticsVm(
            binding,
            $"{Environment.Version} ({_environment.EnvironmentName})",
            "net8.0",
            sqliteStatus,
            vaultAvailable ? "Available / DPAPI protected" : "Unavailable / Not verified",
            integrated ? "Integrated / SQLite + Vault" : "Preview / in-memory",
            ModuleNames.ToDictionary(
                name => name,
                name => name == "Diagnostics"
                    ? "Loaded / Active"
                    : $"{moduleState} ({registeredModules} persistence contributors)",
                StringComparer.OrdinalIgnoreCase));
    }

    private async Task<string> ReadSqliteStatusAsync(bool integrated, CancellationToken cancellationToken)
    {
        if (!integrated)
        {
            return "Preview mode / SQLite not used";
        }

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? $"Connected / {Path.GetFileName(_paths.DatabaseFile)}"
                : "Unavailable / connection failed";
        }
        catch
        {
            return "Unavailable / connection failed";
        }
    }

    private string ReadBinding()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is { Count: > 0 })
        {
            return string.Join("; ", addresses.OrderBy(address => address, StringComparer.OrdinalIgnoreCase));
        }

        return _configuration["ASPNETCORE_URLS"] ?? "Not verified by active server feature";
    }
}
