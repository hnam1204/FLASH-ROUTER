using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class Be04RMultiConnectionTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task SqliteReaderReturnsAllOwnedConnectionsWithStableMetadataOnly()
    {
        var accountA = await CreateAccountAsync("same@example.test", "Account A", "openai");
        var accountB = await CreateAccountAsync("same@example.test", "Account B", "google");
        var now = DateTimeOffset.UtcNow;

        var historicalApiId = Guid.NewGuid();
        var currentApiId = Guid.NewGuid();
        var oauthId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        await using (var context = await _database.ContextFactory.CreateDbContextAsync())
        {
            context.Add(new ProviderConnectionRecord(
                historicalApiId,
                accountA,
                "openrouter",
                ConnectionKind.ApiKey,
                "Historical OpenRouter",
                null,
                ConnectionStatus.Disconnected,
                now.AddDays(-4),
                now.AddDays(-3)));
            context.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                historicalApiId,
                "openrouter",
                "cred-historical",
                ProviderCredentialKind.ApiKey,
                "vault://connections/historical",
                "workspace-a",
                now.AddDays(-4),
                now.AddDays(-3)));

            context.Add(new ProviderConnectionRecord(
                currentApiId,
                accountA,
                "openai-platform",
                ConnectionKind.ApiKey,
                "Current OpenAI",
                null,
                ConnectionStatus.Authorized,
                now.AddDays(-2),
                now.AddDays(-1)));
            context.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                currentApiId,
                "openai-platform",
                "cred-current",
                ProviderCredentialKind.AdminApiKey,
                "vault://connections/current",
                "org-a",
                now.AddDays(-2),
                now.AddDays(-1)));

            var registrationId = Guid.NewGuid();
            context.Add(new ProviderConnectionRecord(
                oauthId,
                accountA,
                "openai-oauth",
                ConnectionKind.WebOAuth,
                "ChatGPT registration",
                "provider-subject-a",
                ConnectionStatus.Authorized,
                now.AddDays(-1),
                now.AddDays(-1)));
            context.Add(new OAuthRegistrationRecord(
                registrationId,
                accountA,
                "openai-oauth",
                "issued-client-a",
                "https://auth.example.test",
                "FLASH ROUTER TEST",
                "provider-subject-a",
                now.AddDays(-1),
                now.AddDays(-1)));
            context.Add(new ConnectionSessionRecord(
                Guid.NewGuid(),
                accountA,
                "openai-oauth",
                registrationId,
                oauthId,
                "provider-subject-a",
                "issued-client-a",
                "https://auth.example.test",
                "openid email",
                ConnectionStatus.Authorized,
                now.AddDays(-1),
                now.AddHours(2),
                null,
                now.AddHours(-1),
                now.AddHours(-1)));
            context.Add(new TokenVaultReferenceRecord(
                Guid.NewGuid(),
                oauthId,
                "vault://oauth/access",
                "vault://oauth/refresh",
                "vault://oauth/id",
                now.AddDays(-1),
                now.AddDays(-1)));

            context.Add(new ProviderConnectionRecord(
                otherAccountId,
                accountB,
                "openrouter",
                ConnectionKind.ApiKey,
                "Other account",
                null,
                ConnectionStatus.Authorized,
                now,
                now));
            context.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                otherAccountId,
                "openrouter",
                "cred-other",
                ProviderCredentialKind.ApiKey,
                "vault://connections/other",
                null,
                now,
                now));
            await context.SaveChangesAsync();
        }

        var reader = new SqliteAccountConnectionsReader(
            _database.ContextFactory,
            new ProviderRegistry(Array.Empty<IAiProviderAdapter>()));
        var accountConnections = await reader.ListForAccountAsync(accountA);
        var otherConnections = await reader.ListForAccountAsync(accountB);

        Assert.Equal(3, accountConnections.Count);
        Assert.Equal(ConnectionStatus.Authorized, accountConnections[0].Status);
        Assert.Contains(accountConnections, item => item.Status == ConnectionStatus.Disconnected);
        Assert.Contains(accountConnections, item => item.Provenance == ConnectionProvenance.OfficialOAuth);
        Assert.Equal("provider-subject-a", accountConnections.Single(item => item.ConnectionId == oauthId).VerifiedIdentity);
        Assert.Equal("org-a", accountConnections.Single(item => item.ConnectionId == currentApiId).Scope);
        Assert.All(accountConnections, item => Assert.Equal(ConnectionCredentialAvailability.StoredReference, item.CredentialAvailability));
        Assert.DoesNotContain("synthetic-secret", string.Join('|', accountConnections.Select(item => item.ToString())));
        Assert.Single(otherConnections);
        Assert.Equal(accountB, otherConnections[0].AccountId);
        Assert.DoesNotContain(accountConnections, item => item.AccountId != accountA);
    }

    [Fact]
    public async Task InMemoryOAuthListAddsAllConnectionsWithoutChangingLatestSemantics()
    {
        var store = new InMemoryOAuthConnectionStore();
        var accountId = Guid.NewGuid();
        var older = DateTimeOffset.UtcNow.AddMinutes(-10);
        await store.SaveAuthorizedAsync(accountId, Guid.NewGuid(), Guid.NewGuid(), "openai-oauth", "subject-old", "client-old", "host", "agent", new HashSet<string> { "openid" }, older, older.AddHours(1));
        await store.SaveAuthorizedAsync(accountId, Guid.NewGuid(), Guid.NewGuid(), "openai-oauth", "subject-new", "client-new", "host", "agent", new HashSet<string> { "openid", "email" }, older.AddMinutes(5), older.AddHours(2));

        var latest = await store.GetForAccountAsync(accountId);
        var all = await store.ListForAccountAsync(accountId);

        Assert.Equal("subject-new", latest!.ProviderSubject);
        Assert.Equal(2, all.Count);
        Assert.Equal("subject-new", all[0].ProviderSubject);
    }

    [Fact]
    public async Task ObservationReaderKeepsStaleValuesAndUnsupportedStateSeparateFromZero()
    {
        var connectionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var connection = new AccountConnectionSummary(
            accountId,
            connectionId,
            "openrouter",
            "OpenRouter",
            null,
            ConnectionKind.ApiKey,
            ProviderCredentialKind.ApiKey,
            ConnectionStatus.Authorized,
            null,
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            null,
            null,
            null,
            ConnectionCredentialAvailability.StoredReference,
            ConnectionProvenance.ProviderApiCredential,
            []);
        var connections = new StubAccountConnectionsReader(connection);
        var observations = new InMemoryProviderObservationStore();
        var observedAt = DateTimeOffset.UtcNow.AddHours(-2);
        await observations.SaveAsync(new ProviderCheckResult(
            ProviderOperationStatus.Success,
            string.Empty,
            string.Empty,
            "openrouter",
            connectionId,
            observedAt,
            observedAt,
            "correlation-stale",
            [new(
                ProviderCapability.Usage,
                "usage_percent",
                "65",
                "percent",
                null,
                ProviderObservationSource.OfficialApi,
                ProviderObservationScope.IndividualKey,
                observedAt,
                observedAt.AddMinutes(-1),
                ProviderObservationStatus.Confirmed)],
            []));

        var unsupportedConnection = connection with { ConnectionId = Guid.NewGuid(), ProviderId = "gemini", ProviderDisplayName = "Gemini" };
        var reader = new ConnectionObservationsReader(
            new StubAccountConnectionsReader(connection, unsupportedConnection),
            observations);
        await observations.SaveAsync(new ProviderCheckResult(
            ProviderOperationStatus.Unsupported,
            "QUOTA_UNSUPPORTED",
            "Provider does not expose this metric.",
            unsupportedConnection.ProviderId,
            unsupportedConnection.ConnectionId,
            observedAt,
            observedAt,
            "correlation-unsupported",
            [],
            []));

        var result = await reader.ListForAccountAsync(accountId);
        var stale = Assert.Single(result, item => item.ConnectionId == connectionId && item.MetricName == "usage_percent");
        var unsupported = Assert.Single(result, item => item.ConnectionId == unsupportedConnection.ConnectionId);

        Assert.True(stale.IsStale);
        Assert.Equal("65", stale.Value);
        Assert.Equal("OfficialApi", stale.Source);
        Assert.Equal("Unsupported", unsupported.ObservationStatus);
        Assert.NotEqual("0", unsupported.Value);
        Assert.Equal("Unsupported", unsupported.LastCheckStatus);
    }

    [Fact]
    public void CodexCliQuotaSourceIsVisibleButCannotBeAddedAsAGenericApiConnection()
    {
        var registry = new ProviderRegistry(Array.Empty<IAiProviderAdapter>());
        var descriptor = Assert.Single(registry.Descriptors, item => item.ProviderId == "codex-cli");

        Assert.Equal(ProviderImplementationState.Implemented, descriptor.ImplementationState);
        Assert.Contains(ProviderCapability.Quota, descriptor.SupportedCapabilities);
        Assert.False(descriptor.IsConnectable);
        Assert.False(registry.TryResolve("codex-cli", out _));
    }

    private async Task<Guid> CreateAccountAsync(string email, string displayName, string provider)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = displayName,
            Provider = provider,
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email, null, "newest")).Single(item => item.DisplayName == displayName).AccountId;
    }

    private sealed class StubAccountConnectionsReader(params AccountConnectionSummary[] connections) : IAccountConnectionsReader
    {
        public Task<IReadOnlyList<AccountConnectionSummary>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AccountConnectionSummary>>(connections.Where(item => item.AccountId == accountId).ToArray());
        }
    }
}
