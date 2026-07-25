namespace InvestOps.Application.Operations.ListProcessingEvents;

// Datas como DateTime (UTC): o Dapper materializa este record via construtor e o Npgsql
// devolve timestamptz como DateTime. Serializa como ISO-8601 com 'Z'.
public sealed record ProcessingEventListItemDto(
    Guid Id,
    Guid OperationId,
    string ExternalId,
    string EventType,
    string Status,
    int AttemptCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? ProcessedAt);
