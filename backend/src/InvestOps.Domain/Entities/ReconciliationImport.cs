using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public sealed class ReconciliationImport
{
    public required Guid Id { get; init; }
    public required string Source { get; init; }
    public required DateOnly ReferenceDate { get; init; }
    public required ReconciliationImportStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
