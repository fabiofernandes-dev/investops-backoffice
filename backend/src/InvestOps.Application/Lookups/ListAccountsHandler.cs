using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Lookups;

public sealed class ListAccountsHandler(ILookupRepository repository)
{
    public Task<IReadOnlyList<AccountLookupDto>> HandleAsync(CancellationToken ct) => repository.ListAccountsAsync(ct);
}
