namespace InvestOps.Application.Operations.ListOperations;

// Item da lista de operações enriquecido com nomes (join). Datas como DateOnly (via DateOnlyTypeHandler);
// se a materialização por construtor falhar, trocar para string + cast ::text no SQL.
public sealed record OperationListItemDto(
    Guid Id,
    string ExternalId,
    Guid AccountId,
    Guid ProductId,
    string CustomerName,
    string AccountNumber,
    string ProductName,
    string? Ticker,
    string OperationType,
    decimal Quantity,
    decimal UnitPrice,
    decimal GrossValue,
    decimal NetValue,
    DateOnly OperationDate,
    DateOnly SettlementDate,
    string Status,
    string? RejectionReason);
