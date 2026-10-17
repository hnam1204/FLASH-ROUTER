namespace FlashAI.Modules.Activity.Contracts;

public interface IAuditSink
{
    ValueTask WriteAsync(string eventType, string safeSummary, CancellationToken cancellationToken = default);
}
