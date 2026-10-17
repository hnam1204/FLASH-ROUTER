namespace FlashAI.Modules.Diagnostics.Contracts;

public interface IDiagnosticsReader
{
    ValueTask<DiagnosticsSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record DiagnosticsSnapshot(string Binding, string Storage, string Modules, string Secrets);
