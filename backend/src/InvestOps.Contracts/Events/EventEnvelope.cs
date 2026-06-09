namespace InvestOps.Contracts.Events;

public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    TPayload Payload);
