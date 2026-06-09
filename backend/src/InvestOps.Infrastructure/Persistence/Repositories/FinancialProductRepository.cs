using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class FinancialProductRepository(IDbSession session) : IFinancialProductRepository
{
    public async Task<FinancialProduct?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM financial_products WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<FinancialProduct>(
            new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
    }
}
