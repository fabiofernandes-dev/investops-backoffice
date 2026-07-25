using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Products;

public sealed class DeleteProductHandler(IFinancialProductRepository repository)
{
    public async Task<bool> HandleAsync(Guid id, CancellationToken ct) => await repository.DeactivateAsync(id, ct) > 0;
}
