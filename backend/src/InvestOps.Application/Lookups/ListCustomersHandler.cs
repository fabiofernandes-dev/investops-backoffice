using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Lookups;

public sealed class ListCustomersHandler(ILookupRepository repository)
{
    public Task<IReadOnlyList<CustomerLookupDto>> HandleAsync(CancellationToken ct) => repository.ListCustomersAsync(ct);
}
