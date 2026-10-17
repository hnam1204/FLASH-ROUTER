using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class PreviewConnectionUiPort : IConnectionUiPort
{
    public Task<ConnectionStatusVm> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var vm = new ConnectionStatusVm(
            accountId,
            "OpenAI ChatGPT Official",
            "NotConfigured",
            "Chưa cấu hình",
            null,
            null,
            null,
            null,
            "Official OAuth chưa được bật trong chế độ xem trước.",
            false
        );

        return Task.FromResult(vm);
    }

    public Task<OperationResultVm> RequestConnectAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResultVm.Failure(
            "BACKEND_PENDING",
            "Tính năng OAuth chính thức chưa được cấu hình backend. Thao tác được ghi nhận trong chế độ xem trước."));
    }

    public Task<OperationResultVm> RequestReconnectAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResultVm.Failure(
            "BACKEND_PENDING",
            "Tính năng xác thực lại chưa được cấu hình backend. Thao tác được ghi nhận trong chế độ xem trước."));
    }

    public Task<OperationResultVm> RequestDisconnectAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResultVm.Success(
            "Đã ngắt phiên kết nối trong bộ nhớ xem trước."));
    }
}
