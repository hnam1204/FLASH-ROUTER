namespace FlashAI.Modules.Subscriptions.Contracts;

public interface ISubscriptionSummaryReader
{
    ValueTask<SubscriptionSummary?> FindAsync(Guid accountId, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionSummary(string Label, string Source, DateTimeOffset ObservedAt, string Confidence);
