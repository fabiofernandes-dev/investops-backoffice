using InvestOps.Application.Lookups;

namespace InvestOps.Application.Abstractions;

public interface ILookupRepository
{
    Task<IReadOnlyList<ProductLookupDto>> ListProductsAsync(CancellationToken ct);
    Task<IReadOnlyList<CustomerLookupDto>> ListCustomersAsync(CancellationToken ct);
    Task<IReadOnlyList<AccountLookupDto>> ListAccountsAsync(CancellationToken ct);
}
