namespace InvestOps.Application.Operations.ImportOperation;

public sealed record ImportOperationRequest(
    string ExternalId,
    Guid AccountId,
    Guid ProductId,
    string OperationType,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly OperationDate);
