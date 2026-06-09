using System.Data.Common;

namespace InvestOps.Infrastructure.Persistence;

public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken ct = default);
}
