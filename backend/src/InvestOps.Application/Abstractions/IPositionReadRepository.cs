using InvestOps.Application.Positions;

namespace InvestOps.Application.Abstractions;

public interface IPositionReadRepository
{
    Task<(IReadOnlyList<PositionDto> Items, int Total)> ListAsync(PositionsQuery query, CancellationToken ct);
    Task<IReadOnlyList<PositionDto>> GetByAccountAsync(Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<PositionDto>> GetByCustomerAsync(Guid customerId, CancellationToken ct);
}
