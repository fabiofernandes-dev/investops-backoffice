using Dapper;
using InvestOps.Application.Abstractions;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class InvestmentAccountRepository(IDbSession session) : IInvestmentAccountRepository
{
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM investment_accounts WHERE id = @id);";
        var conn = await session.GetConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
    }
}
