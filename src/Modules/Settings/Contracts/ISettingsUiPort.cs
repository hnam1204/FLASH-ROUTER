using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Settings.Contracts;

public sealed class UserSettingsVm
{
    public string Theme { get; set; } = "light";

    public bool MaskAccountEmails { get; set; } = false;

    public bool HideSensitiveMetadata { get; set; } = false;

    public bool HideTotpByDefault { get; set; } = true;

    public bool AutoCheckConnections { get; set; } = false;

    public UserSettingsVm Clone() => new()
    {
        Theme = Theme,
        MaskAccountEmails = MaskAccountEmails,
        HideSensitiveMetadata = HideSensitiveMetadata,
        HideTotpByDefault = HideTotpByDefault,
        AutoCheckConnections = AutoCheckConnections
    };
}

public interface ISettingsUiPort
{
    Task<UserSettingsVm> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<OperationResultVm> SaveSettingsAsync(UserSettingsVm settings, CancellationToken cancellationToken = default);
}
