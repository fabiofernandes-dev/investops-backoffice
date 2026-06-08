using System.Data.Common;

namespace InvestOps.Application.Abstractions;

public interface IInvestmentAccountRepository
{
    Task<bool> ExistsAsync(DbConnection conn, Guid id, CancellationToken ct);
}
