using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public sealed class ReconciliationDivergence
{
    public required Guid Id { get; init; }
    public required Guid ReconciliationImportId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid ProductId { get; init; }
    public required decimal InternalQuantity { get; init; }
    public required decimal CustodianQuantity { get; init; }
    public required DivergenceStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}
