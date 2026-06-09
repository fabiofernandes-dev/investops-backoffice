using InvestOps.Domain.Entities;

namespace InvestOps.Application.Operations;

public sealed record OperationDto(
    Guid Id,
    string ExternalId,
    Guid AccountId,
    Guid ProductId,
    string OperationType,
    decimal Quantity,
    decimal UnitPrice,
    decimal GrossValue,
    decimal NetValue,
    DateOnly OperationDate,
    DateOnly SettlementDate,
    string Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OperationDto From(Operation o) => new(
        o.Id, o.ExternalId, o.AccountId, o.ProductId, o.OperationType.ToString(),
        o.Quantity, o.UnitPrice, o.GrossValue, o.NetValue, o.OperationDate, o.SettlementDate,
        o.Status.ToString(), o.RejectionReason?.ToString(), o.CreatedAt, o.UpdatedAt);
}
