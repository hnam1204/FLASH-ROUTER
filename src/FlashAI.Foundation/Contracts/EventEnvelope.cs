namespace FlashAI.Foundation.Contracts;

public sealed record EventEnvelope<T>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    T Payload);
