using InvestOps.Application.Products;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IFinancialProductRepository
{
    Task<FinancialProduct?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct);
    Task InsertAsync(FinancialProduct product, CancellationToken ct);
    Task<int> UpdateAsync(FinancialProduct product, CancellationToken ct);
    Task<int> DeactivateAsync(Guid id, CancellationToken ct);
}
