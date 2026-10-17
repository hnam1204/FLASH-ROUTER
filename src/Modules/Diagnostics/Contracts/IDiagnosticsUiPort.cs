namespace FlashAI.Modules.Diagnostics.Contracts;

public sealed record DiagnosticsVm(
    string HostBinding,
    string SdkVersion,
    string TargetFramework,
    string DurableStorageStatus,
    string VaultStatus,
    string Mode,
    IReadOnlyDictionary<string, string> ModuleStatuses);

public interface IDiagnosticsUiPort
{
    Task<DiagnosticsVm> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
