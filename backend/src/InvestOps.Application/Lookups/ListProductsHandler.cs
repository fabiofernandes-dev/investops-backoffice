using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Lookups;

public sealed class ListProductsHandler(ILookupRepository repository)
{
    public Task<IReadOnlyList<ProductLookupDto>> HandleAsync(CancellationToken ct) => repository.ListProductsAsync(ct);
}
