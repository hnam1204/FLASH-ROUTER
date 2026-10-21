using System.Collections.Concurrent;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Application;

public sealed class AccountUsageRefreshService : IAccountUsageRefreshPort
{
    private static readonly IReadOnlySet<ProviderCapability> UsageCapabilities = new HashSet<ProviderCapability>
    {
        ProviderCapability.Usage,
        ProviderCapability.TokenUsage,
        ProviderCapability.Costs,
        ProviderCapability.Credits,
        ProviderCapability.Balance,
        ProviderCapability.RateLimits,
        ProviderCapability.Quota
    };

    private readonly IAccountConnectionsReader _connections;
    private readonly IProviderSyncOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _accountGates = new();

    public AccountUsageRefreshService(
        IAccountConnectionsReader connections,
        IProviderSyncOrchestrator orchestrator)
    {
        _connections = connections;
        _orchestrator = orchestrator;
    }

    public async Task<AccountUsageRefreshResult> RefreshAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var gate = _accountGates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return new AccountUsageRefreshResult(
                accountId,
                "AlreadyRunning",
                "Một lần làm mới Usage & Quota của account này đang chạy.",
                0,
                0,
                true);
        }

        try
        {
            var connections = await _connections.ListForAccountAsync(accountId, cancellationToken);
            var candidates = connections
                .Where(IsRefreshable)
                .ToArray();
            var skipped = connections.Count - candidates.Length;
            var checkedConnections = 0;
            var failedConnections = 0;

            foreach (var connection in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _orchestrator.CheckConnectionAsync(
                        connection.ConnectionId,
                        UsageCapabilities,
                        allowBillableRead: true,
                        cancellationToken);
                    checkedConnections++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Preserve other connection observations and let the UI show the historical snapshot.
                    failedConnections++;
                }
            }

            if (checkedConnections == 0 && failedConnections == 0)
            {
                return new AccountUsageRefreshResult(
                    accountId,
                    "Unsupported",
                    "Không có connection API được cấp quyền để đọc Usage & Quota; ChatGPT Web vẫn giữ trạng thái Unsupported.",
                    0,
                    skipped);
            }

            if (checkedConnections == 0)
            {
                return new AccountUsageRefreshResult(
                    accountId,
                    "Failed",
                    "Không thể làm mới các connection API; dữ liệu lịch sử được giữ nguyên.",
                    0,
                    skipped,
                    FailedConnections: failedConnections);
            }

            return new AccountUsageRefreshResult(
                accountId,
                "Completed",
                $"Đã làm mới Usage & Quota cho {checkedConnections} connection API{(failedConnections > 0 ? $"; {failedConnections} connection lỗi" : string.Empty)}; dữ liệu không có nguồn vẫn để trống.",
                checkedConnections,
                skipped,
                FailedConnections: failedConnections);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AccountUsageRefreshResult(
                accountId,
                "Cancelled",
                "Đã hủy làm mới Usage & Quota; quan sát lịch sử được giữ nguyên.",
                0,
                0);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsRefreshable(AccountConnectionSummary connection)
        => connection.ConnectionKind != ConnectionKind.Manual
            && connection.Status is not (ConnectionStatus.Disconnected
                or ConnectionStatus.Revoked
                or ConnectionStatus.Unauthorized)
            && connection.CredentialAvailability == ConnectionCredentialAvailability.StoredReference;
}
