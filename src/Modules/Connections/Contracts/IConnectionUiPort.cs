using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Connections.Contracts;

public interface IConnectionUiPort
{
    Task<ConnectionStatusVm> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RequestConnectAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RequestReconnectAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RequestDisconnectAsync(Guid accountId, CancellationToken cancellationToken = default);
}
