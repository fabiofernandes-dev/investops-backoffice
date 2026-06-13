using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;

namespace InvestOps.Application.Positions;

public sealed class ListPositionsHandler(IPositionReadRepository repository)
{
    public async Task<PagedResult<PositionDto>> HandleAsync(PositionsQuery query, CancellationToken ct)
    {
        var (items, total) = await repository.ListAsync(query, ct);
        return new PagedResult<PositionDto>(items, total, query.NormalizedPage, query.NormalizedPageSize);
    }
}
