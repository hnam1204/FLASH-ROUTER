namespace FlashAI.Modules.Settings.Contracts;

public interface ISettingsReader
{
    ValueTask<SettingsSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record SettingsSnapshot(string Theme, bool PrivacyMode, bool BackgroundJobsEnabled);
