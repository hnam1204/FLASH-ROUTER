using FlashAI.Modules.Connections.Application;

namespace FlashAI.IntegrationTests;

public sealed class CodexCliUsageRefreshTests
{
    [Theory]
    [InlineData("owner@example.test")]
    [InlineData("")]
    public async Task ReportsUnsupportedQuotaSourceWithoutClaimingSuccess(string identity)
    {
        var accountId = Guid.NewGuid();
        var service = new CodexCliUsageRefreshService();

        var result = await service.RefreshAsync(accountId, identity);

        Assert.Equal(accountId, result.AccountId);
        Assert.Equal("CODEX_CLI_SOURCE_UNSUPPORTED", result.Status);
        Assert.Equal(0, result.CheckedConnections);
        Assert.Equal(1, result.FailedConnections);
        Assert.Contains("/status", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsCancellationWithoutReadingOrChangingAnyAccountData()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new CodexCliUsageRefreshService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RefreshAsync(Guid.NewGuid(), "owner@example.test", cancellation.Token));
    }
}
