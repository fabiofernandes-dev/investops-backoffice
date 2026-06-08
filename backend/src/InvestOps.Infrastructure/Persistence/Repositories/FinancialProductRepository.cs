using System.Data.Common;
using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class FinancialProductRepository : IFinancialProductRepository
{
    public Task<FinancialProduct?> GetByIdAsync(DbConnection conn, Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM financial_products WHERE id = @id;";
        return conn.QuerySingleOrDefaultAsync<FinancialProduct>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
