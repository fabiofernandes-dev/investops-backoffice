using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Positions;

public sealed class GetPositionsByCustomerHandler(IPositionReadRepository repository)
{
    public Task<IReadOnlyList<PositionDto>> HandleAsync(Guid customerId, CancellationToken ct)
        => repository.GetByCustomerAsync(customerId, ct);
}
