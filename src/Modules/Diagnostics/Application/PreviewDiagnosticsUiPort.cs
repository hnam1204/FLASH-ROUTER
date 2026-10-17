using FlashAI.Modules.Diagnostics.Contracts;

namespace FlashAI.Modules.Diagnostics.Application;

public sealed class PreviewDiagnosticsUiPort : IDiagnosticsUiPort
{
    public Task<DiagnosticsVm> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var vm = new DiagnosticsVm(
            "127.0.0.1:5270 / localhost (Local-only guard active)",
            Environment.Version.ToString(),
            "net8.0 (Compatibility target)",
            "Scaffold / In-Memory Preview",
            "Vault Prototype (DPAPI scaffold)",
            "DEVELOPMENT PREVIEW (DEMO DATA)",
            new Dictionary<string, string>
            {
                ["Accounts"] = "UI Ready / In-Memory Preview",
                ["Connections"] = "UI Ready / Backend Pending",
                ["Subscriptions"] = "UI Ready / In-Memory Preview",
                ["Authenticator"] = "UI Ready / In-Memory Preview",
                ["Reminders"] = "UI Ready / In-Memory Preview",
                ["Activity"] = "UI Ready / In-Memory Preview",
                ["ImportExport"] = "UI Ready / Backend Pending",
                ["BackupRestore"] = "UI Ready / Backend Pending",
                ["Settings"] = "UI Ready / Circuit State",
                ["Diagnostics"] = "UI Ready / Active"
            }
        );

        return Task.FromResult(vm);
    }
}
