using System.Data.Common;

namespace InvestOps.Application.Abstractions;

public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken ct = default);
}
