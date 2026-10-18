using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Activity.Application;
using FlashAI.Modules.Activity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class ActivityJournalTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task DurableJournalPersistsAndFiltersEventsByAccount()
    {
        var firstAccountId = await CreateAccountAsync("journal-one@example.test");
        var secondAccountId = await CreateAccountAsync("journal-two@example.test");
        var sink = new SqliteAuditSink(_database.ContextFactory);

        await sink.WriteAsync(
            firstAccountId,
            "CredentialUpdated",
            "Credential metadata updated without exposing secret material.",
            "Success",
            "password=[REDACTED]",
            "attempt-one");
        await sink.WriteAsync(
            secondAccountId,
            "BrowserAuthFailed",
            "Browser authentication stopped for user action.",
            "Warning",
            "challenge=required",
            "attempt-two");

        var reader = new SqliteActivityUiPort(_database.ContextFactory);
        var firstEvents = await reader.GetEventsAsync(firstAccountId);
        var secondEvents = await reader.GetEventsAsync(secondAccountId, "Warning");

        var firstEvent = Assert.Single(firstEvents);
        Assert.Equal("CredentialUpdated", firstEvent.EventType);
        Assert.Equal("Success", firstEvent.Severity);
        Assert.Equal("password=[REDACTED]", firstEvent.SafeDetails);
        var secondEvent = Assert.Single(secondEvents);
        Assert.Equal("BrowserAuthFailed", secondEvent.EventType);
        Assert.Equal("challenge=required", secondEvent.SafeDetails);
    }

    [Fact]
    public async Task HistoricalJournalCanFilterByAttemptWithoutDroppingOtherAccountHistory()
    {
        var accountId = await CreateAccountAsync("journal-attempt@example.test");
        var selectedAttempt = Guid.NewGuid();
        var otherAttempt = Guid.NewGuid();
        var sink = new SqliteAuditSink(_database.ContextFactory);

        await sink.WriteAsync(accountId, "AttemptStarted", "Selected attempt started.", correlationId: "selected", attemptId: selectedAttempt);
        await sink.WriteAsync(accountId, "AttemptFailed", "Other attempt failed.", correlationId: "other", attemptId: otherAttempt);

        var reader = new SqliteActivityUiPort(_database.ContextFactory);
        var all = await reader.GetEventsAsync(accountId);
        var selected = await reader.GetEventsAsync(accountId, attemptId: selectedAttempt);

        Assert.Equal(2, all.Count);
        var eventForAttempt = Assert.Single(selected);
        Assert.Equal(selectedAttempt, eventForAttempt.AttemptId);
        Assert.Equal("AttemptStarted", eventForAttempt.EventType);
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = "Activity journal test",
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });

        Assert.True(result.IsSuccess, result.Message);
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }
}
