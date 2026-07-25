using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Lookups;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class LookupRepository(IDbSession session) : ILookupRepository
{
    public async Task<IReadOnlyList<CustomerLookupDto>> ListCustomersAsync(CancellationToken ct)
    {
        const string sql = "SELECT id, name FROM customers ORDER BY name;";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<CustomerLookupDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<AccountLookupDto>> ListAccountsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT a.id, a.account_number, c.name AS customer_name
            FROM investment_accounts a
            JOIN customers c ON c.id = a.customer_id
            ORDER BY a.account_number;
            """;
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<AccountLookupDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
