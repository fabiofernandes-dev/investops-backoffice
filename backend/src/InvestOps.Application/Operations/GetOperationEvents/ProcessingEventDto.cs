namespace InvestOps.Application.Operations.GetOperationEvents;

// Datas como DateTime (UTC): o Dapper materializa este record via construtor e o Npgsql
// devolve timestamptz como DateTime. Serializa como ISO-8601 com 'Z'.
public sealed record ProcessingEventDto(
    string EventType,
    string Status,
    int AttemptCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? ProcessedAt);
