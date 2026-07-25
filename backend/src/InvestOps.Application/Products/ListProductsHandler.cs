using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Products;

public sealed class ListProductsHandler(IFinancialProductRepository repository)
{
    public Task<IReadOnlyList<ProductDto>> HandleAsync(CancellationToken ct) => repository.ListAsync(ct);
}
