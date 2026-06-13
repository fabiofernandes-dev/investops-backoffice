using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Lookups;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class LookupRepository(IDbSession session) : ILookupRepository
{
    public async Task<IReadOnlyList<ProductLookupDto>> ListProductsAsync(CancellationToken ct)
    {
        const string sql = "SELECT id, name, ticker FROM financial_products ORDER BY name;";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProductLookupDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerLookupDto>> ListCustomersAsync(CancellationToken ct)
    {
        const string sql = "SELECT id, name FROM customers ORDER BY name;";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<CustomerLookupDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
