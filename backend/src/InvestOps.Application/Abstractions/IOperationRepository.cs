using InvestOps.Application.Operations.ListOperations;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IOperationRepository
{
    Task<Operation?> GetByExternalIdAsync(string externalId, CancellationToken ct);
    Task InsertAsync(Operation op, CancellationToken ct);
    Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<(IReadOnlyList<OperationListItemDto> Items, int Total)> ListAsync(ListOperationsQuery query, CancellationToken ct);
}
