using InvestOps.Application.Lookups;

namespace InvestOps.Application.Abstractions;

public interface ILookupRepository
{
    Task<IReadOnlyList<CustomerLookupDto>> ListCustomersAsync(CancellationToken ct);
    Task<IReadOnlyList<AccountLookupDto>> ListAccountsAsync(CancellationToken ct);
}
