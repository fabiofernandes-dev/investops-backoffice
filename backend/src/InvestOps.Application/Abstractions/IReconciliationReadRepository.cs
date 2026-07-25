using InvestOps.Application.Common;
using InvestOps.Application.Reconciliation.ListDivergences;
using InvestOps.Domain.Reconciliation;

namespace InvestOps.Application.Abstractions;

public interface IReconciliationReadRepository
{
    Task<PagedResult<DivergenceDto>> ListDivergencesAsync(DivergencesQuery query, CancellationToken ct);
    Task<IReadOnlyList<InternalPosition>> GetInternalPositionsAsync(
        IReadOnlyList<Guid> accountIds, IReadOnlyList<Guid> productIds, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetExistingAccountIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetExistingProductIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
}
