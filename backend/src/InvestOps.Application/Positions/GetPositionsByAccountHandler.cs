using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Positions;

public sealed class GetPositionsByAccountHandler(IPositionReadRepository repository)
{
    public Task<IReadOnlyList<PositionDto>> HandleAsync(Guid accountId, CancellationToken ct)
        => repository.GetByAccountAsync(accountId, ct);
}
