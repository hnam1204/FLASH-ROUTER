using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Settings.Contracts;

namespace FlashAI.Modules.Settings.Application;

public sealed class PreviewSettingsUiPort : ISettingsUiPort
{
    private UserSettingsVm _settings = new();

    public Task<UserSettingsVm> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings.Clone());
    }

    public Task<OperationResultVm> SaveSettingsAsync(UserSettingsVm settings, CancellationToken cancellationToken = default)
    {
        _settings = settings.Clone();
        return Task.FromResult(OperationResultVm.Success("Đã áp dụng cấu hình cài đặt trong phiên làm việc."));
    }
}
