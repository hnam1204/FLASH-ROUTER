using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Settings.Application;

public sealed class SettingsBoundary
{
    public Result RejectUntilPreferencesStoreExists()
        => Result.Failure("SETTINGS_NOT_ENABLED", "Durable preferences are deferred to the durable data phase.");
}
