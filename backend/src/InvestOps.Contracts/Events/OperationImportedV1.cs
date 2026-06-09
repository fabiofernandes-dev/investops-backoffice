namespace InvestOps.Contracts.Events;

public sealed record OperationImportedV1(
    Guid OperationId,
    string ExternalId,
    Guid AccountId,
    Guid ProductId,
    string OperationType,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly OperationDate)
{
    public const string EventType = "OperationImported";
    public const int Version = 1;
}
