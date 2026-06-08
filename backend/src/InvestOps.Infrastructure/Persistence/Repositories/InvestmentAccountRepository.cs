using System.Data.Common;
using Dapper;
using InvestOps.Application.Abstractions;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class InvestmentAccountRepository : IInvestmentAccountRepository
{
    public Task<bool> ExistsAsync(DbConnection conn, Guid id, CancellationToken ct)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM investment_accounts WHERE id = @id);";
        return conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
