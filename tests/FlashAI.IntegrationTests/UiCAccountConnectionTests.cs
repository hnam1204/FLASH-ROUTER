using System.Text;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Integration;
using FlashAI.Modules.Connections.Persistence;
using FlashAI.Modules.ImportExport.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class UiCAccountConnectionTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();
    private readonly string _vaultRoot = Path.Combine(Path.GetTempPath(), "flash-router-ui-c-vault-" + Guid.NewGuid().ToString("N"));
    private IDataProtectionProvider _dataProtection = null!;
    private ISecretVault _vault = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _dataProtection = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_vaultRoot, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER UI-C TEST"));
        _vault = new FileSecretVault(Path.Combine(_vaultRoot, "vault"), _dataProtection);
    }

    public async Task DisposeAsync()
    {
        (_vault as IDisposable)?.Dispose();
        await _database.DisposeAsync();
        if (Directory.Exists(_vaultRoot))
        {
            Directory.Delete(_vaultRoot, recursive: true);
        }
    }

    [Fact]
    public Task CsvPreviewHandlesQuotedFieldsAndBlankInputWithoutSyntheticRows()
    {
        var content = "Email,DisplayName,Provider,RecordedPlan,Tags\r\n"
            + "user@example.test,\"A, B\",openai,Plus,\"personal,work\"\r\n"
            + "quoted@example.test,\"A \"\"quoted\"\" name\",openai,Free,\"tag\"";

        var preview = CsvMetadataParser.Parse("accounts.csv", content);

        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(2, preview.ValidRows);
        Assert.Equal("A, B", preview.PreviewRows[0].DisplayName);
        Assert.Equal("personal,work", preview.PreviewRows[0].Tags);
        Assert.Equal("A \"quoted\" name", preview.PreviewRows[1].DisplayName);

        var blank = CsvMetadataParser.Parse("blank.csv", "");
        Assert.Equal(0, blank.TotalRows);
        Assert.Empty(blank.PreviewRows);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CredentialSaveStoresSecretInVaultAndOnlyMetadataInSqlite()
    {
        var accountId = await CreateAccountAsync("credential@example.test");
        var password = "P@ssword-UI-C-123";
        var port = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);

        var result = await port.SaveAsync(
            accountId,
            new ChatGptCredentialInput(
                "credential@example.test",
                password,
                "JBSWY3DPEHPK3PXP",
                OpenBrowserAfterSave: true));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.ConnectionId);
        Assert.Equal("CredentialStored", result.Status);

        var vaultKey = SqliteAccountCredentialUiPort.GetVaultKey(result.ConnectionId!.Value);
        var stored = await _vault.ReadAsync(vaultKey);
        Assert.NotNull(stored);
        Assert.Contains(password, Encoding.UTF8.GetString(stored.Value.Span), StringComparison.Ordinal);

        await using var context = await _database.ContextFactory.CreateDbContextAsync();
        var connection = await context.Set<ProviderConnectionRecord>().SingleAsync();
        var reference = await context.Set<ProviderCredentialReferenceRecord>().SingleAsync();
        Assert.Equal(ConnectionStatus.CredentialStored, connection.Status);
        Assert.Null(connection.ExternalIdentity);
        Assert.Equal(ProviderCredentialKind.ManualWebCredential, reference.CredentialKind);
        Assert.Equal(vaultKey, reference.VaultKey);
        Assert.Empty(await context.Set<BrowserProfileBindingRecord>().ToListAsync());

        var columns = await SqliteAccountDatabaseFixture.ReadColumnsAsync(context, "ProviderCredentialReferences");
        Assert.DoesNotContain(columns, column => column.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("totp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("secret", StringComparison.OrdinalIgnoreCase));

        var storedText = await ReadCredentialReferenceTextAsync(context);
        Assert.DoesNotContain(password, storedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualCredentialIsLocalMetadataAndNeverVerifiedIdentity()
    {
        var accountId = await CreateAccountAsync("manual-metadata@example.test");
        var port = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);

        var result = await port.SaveAsync(
            accountId,
            new ChatGptCredentialInput(
                "manual-metadata@example.test",
                "P@ssword-metadata",
                null,
                OpenBrowserAfterSave: false));

        Assert.True(result.IsSuccess, result.Message);

        var reader = new SqliteAccountConnectionsReader(
            _database.ContextFactory,
            new ProviderRegistry(Array.Empty<IAiProviderAdapter>()));
        var connection = Assert.Single(await reader.ListForAccountAsync(accountId));

        Assert.Equal(ConnectionKind.Manual, connection.ConnectionKind);
        Assert.Equal(ConnectionProvenance.LocalMetadata, connection.Provenance);
        Assert.Null(connection.VerifiedIdentity);
        Assert.Equal("manual-metadata@example.test", connection.UserProvidedIdentity);
    }

    [Fact]
    public async Task CredentialSaveRemovesVaultSecretWhenSqliteCommitFails()
    {
        var port = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);

        var result = await port.SaveAsync(
            Guid.NewGuid(),
            new ChatGptCredentialInput(
                "orphan@example.test",
                "P@ssword-rollback",
                null,
                OpenBrowserAfterSave: false));

        Assert.False(result.IsSuccess);
        Assert.Equal("SQLITE_COMMIT_FAILED", result.Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(_vaultRoot, "vault"), "*.secret"));

        await using var context = await _database.ContextFactory.CreateDbContextAsync();
        Assert.Empty(await context.Set<ProviderConnectionRecord>().ToListAsync());
        Assert.Empty(await context.Set<ProviderCredentialReferenceRecord>().ToListAsync());
    }

    [Fact]
    public async Task OfficialLoginIsBlockedUntilEligibilityIsConfirmed()
    {
        var authorization = new CapturingAuthorizationPort();
        var eligibility = new OfficialOAuthEligibility(OpenAiOAuthOptions.BlockedDefault);
        var port = new OfficialLoginUiPort(authorization, eligibility);

        var result = await port.StartAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("OAUTH_ELIGIBILITY_BLOCKED", result.Status);
        Assert.Equal(0, authorization.CallCount);
    }

    [Fact]
    public async Task OfficialLoginDoesNotTreatAccountEmailAsProviderSubject()
    {
        var authorization = new CapturingAuthorizationPort
        {
            Result = Result<OAuthAuthorizationRequest>.Failure("OFFLINE_TEST", "No browser launch in this test.")
        };
        var options = new OpenAiOAuthOptions("FLASH ROUTER", true, TimeSpan.FromMinutes(5));
        var port = new OfficialLoginUiPort(authorization, new OfficialOAuthEligibility(options));

        var result = await port.StartAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("OFFLINE_TEST", result.Status);
        Assert.Null(authorization.ExpectedProviderSubject);
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = "UI-C test account",
            Provider = "openai",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });

        Assert.True(result.IsSuccess, result.Message);
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }

    private static async Task<string> ReadCredentialReferenceTextAsync(FlashAiDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ProviderId, CredentialId, CredentialKind, VaultKey, COALESCE(Scope, '') FROM ProviderCredentialReferences;";
            await using var reader = await command.ExecuteReaderAsync();
            var values = new List<string>();
            while (await reader.ReadAsync())
            {
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    values.Add(reader.GetString(index));
                }
            }

            return string.Join("|", values);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private sealed class CapturingAuthorizationPort : IOfficialAuthorizationPort
    {
        public int CallCount { get; private set; }
        public string? ExpectedProviderSubject { get; private set; }
        public Result<OAuthAuthorizationRequest> Result { get; init; }
            = Result<OAuthAuthorizationRequest>.Failure("NOT_CONFIGURED", "Not configured.");

        public Task<Result<OAuthAuthorizationRequest>> StartAsync(
            Guid accountId,
            string? issuedClientId = null,
            Guid? registrationId = null,
            string? expectedProviderSubject = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ExpectedProviderSubject = expectedProviderSubject;
            return Task.FromResult(Result);
        }

        public Task<OAuthConnectionResult> HandleCallbackAsync(
            Guid attemptId,
            OAuthCallbackInput callback,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OAuthConnectionResult.Failure("NOT_IMPLEMENTED", "Not used in this test."));
    }
}
