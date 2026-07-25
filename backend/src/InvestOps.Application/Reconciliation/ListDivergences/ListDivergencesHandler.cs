using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;

namespace InvestOps.Application.Reconciliation.ListDivergences;

public sealed class ListDivergencesHandler(IReconciliationReadRepository read)
{
    public Task<PagedResult<DivergenceDto>> HandleAsync(DivergencesQuery query, CancellationToken ct)
        => read.ListDivergencesAsync(query, ct);
}
