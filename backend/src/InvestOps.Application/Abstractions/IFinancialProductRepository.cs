using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IFinancialProductRepository
{
    Task<FinancialProduct?> GetByIdAsync(Guid id, CancellationToken ct);
}
