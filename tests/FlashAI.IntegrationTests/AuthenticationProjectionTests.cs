using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Activity.Application;
using FlashAI.Modules.Activity.Contracts;
using FlashAI.Modules.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class AuthenticationProjectionTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AuthenticationProjectionPersistsAndLoadsAfterContextRecreation()
    {
        var accountId = await CreateAccountAsync("projection@example.test");
        await using (var context = await _database.ContextFactory.CreateDbContextAsync())
        {
            var account = await context.Set<FlashAI.Modules.Accounts.Domain.AccountRecord>()
                .SingleAsync(item => item.Id == accountId);
            Assert.Equal("NotStarted", account.AuthenticationState);
            Assert.Equal("NotChecked", account.IdentityVerificationState);
        }

        var attemptId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow;
        var store = new SqliteAccountAuthenticationStateStore(_database.ContextFactory);
        await store.RecordAsync(new AccountAuthenticationStateUpdate(
            accountId,
            attemptId,
            "openai-chatgpt",
            "AuthenticatedSessionConfirmed",
            "Unknown",
            observedAt.AddSeconds(-2),
            BrowserSessionObservedAtUtc: observedAt,
            AuthenticatedSessionConfirmedAtUtc: observedAt,
            ObservedAtUtc: observedAt,
            EvidenceSource: "synthetic authenticated application shell"));

        await using var scope = _database.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        var detail = await reader.GetAccountDetailAsync(accountId);

        Assert.NotNull(detail);
        Assert.Equal(attemptId, detail!.LastAuthenticationAttemptId);
        Assert.Equal("AuthenticatedSessionConfirmed", detail.AuthenticationState);
        Assert.Equal("Unknown", detail.IdentityVerificationState);
        Assert.Equal(observedAt, detail.LastBrowserSessionObservedAtUtc);
        Assert.Equal(observedAt, detail.LastAuthenticatedSessionConfirmedAtUtc);
        Assert.Equal("synthetic authenticated application shell", detail.LastObservationEvidenceSource);
    }

    [Fact]
    public async Task DurableActivityWritePublishesOnlyAfterPersistenceWithStructuredFields()
    {
        var accountId = await CreateAccountAsync("activity-stream@example.test");
        var attemptId = Guid.NewGuid();
        var stream = new InProcessActivityEventStream();
        var received = new TaskCompletionSource<ActivityEventVm>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = stream.Subscribe(item =>
        {
            received.TrySetResult(item);
            return Task.CompletedTask;
        });

        var sink = new SqliteAuditSink(_database.ContextFactory, stream);
        await sink.WriteAsync(
            accountId,
            "AuthenticatedSessionConfirmed",
            "Synthetic authenticated session evidence persisted.",
            severity: "Success",
            safeDetails: "identity=unknown",
            correlationId: "attempt-correlation",
            attemptId: attemptId,
            providerId: "openai-chatgpt",
            source: "BrowserAuthOrchestrator",
            outcome: "confirmed",
            durationMs: 321);

        var live = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(attemptId, live.AttemptId);
        Assert.Equal("openai-chatgpt", live.ProviderId);
        Assert.Equal("Success", live.Severity);
        Assert.Equal("confirmed", live.Outcome);
        Assert.Equal(321, live.DurationMs);

        await using var context = await _database.ContextFactory.CreateDbContextAsync();
        var persisted = await context.Set<ActivityEventRecord>().SingleAsync(item => item.Id == live.EventId);
        Assert.Equal(attemptId, persisted.AttemptId);
        Assert.Equal("BrowserAuthOrchestrator", persisted.Source);
        Assert.Equal("confirmed", persisted.Outcome);
        Assert.Equal(321, persisted.DurationMs);
    }

    [Fact]
    public async Task RepeatedUnknownObservationDoesNotAdvanceAccountVersion()
    {
        var accountId = await CreateAccountAsync("projection-unknown@example.test");
        var attemptId = Guid.NewGuid();
        var attemptedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var store = new SqliteAccountAuthenticationStateStore(_database.ContextFactory);
        var update = new AccountAuthenticationStateUpdate(
            accountId,
            attemptId,
            "openai-chatgpt",
            "Unknown",
            "NotChecked",
            attemptedAt,
            ObservedAtUtc: DateTimeOffset.UtcNow,
            EvidenceSource: "synthetic unknown page-state observation");

        await store.RecordAsync(update);
        var first = await ReadAccountAsync(accountId);
        await store.RecordAsync(update with { ObservedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(250) });
        var second = await ReadAccountAsync(accountId);

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.LastObservationAtUtc, second.LastObservationAtUtc);
    }

    private async Task<AccountDetailVm> ReadAccountAsync(Guid accountId)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountDetailAsync(accountId))!;
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = "Projection test",
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }
}
