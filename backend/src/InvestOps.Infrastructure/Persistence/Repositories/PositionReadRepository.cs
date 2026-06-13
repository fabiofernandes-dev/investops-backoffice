using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Positions;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class PositionReadRepository(IDbSession session) : IPositionReadRepository
{
    private const string BaseSelect = """
        SELECT p.id, p.account_id, p.product_id,
               c.name AS customer_name, a.account_number, fp.name AS product_name, fp.ticker,
               p.quantity, p.average_price, p.gross_amount, p.updated_at
        FROM portfolio_positions p
        JOIN investment_accounts a ON a.id = p.account_id
        JOIN customers c           ON c.id = a.customer_id
        JOIN financial_products fp ON fp.id = p.product_id
        """;

    public async Task<(IReadOnlyList<PositionDto> Items, int Total)> ListAsync(PositionsQuery query, CancellationToken ct)
    {
        var filters = new List<string>();
        var parameters = new DynamicParameters();

        if (query.AccountId.HasValue)
        {
            filters.Add("p.account_id = @AccountId");
            parameters.Add("AccountId", query.AccountId.Value);
        }
        if (query.ProductId.HasValue)
        {
            filters.Add("p.product_id = @ProductId");
            parameters.Add("ProductId", query.ProductId.Value);
        }
        if (query.CustomerId.HasValue)
        {
            filters.Add("c.id = @CustomerId");
            parameters.Add("CustomerId", query.CustomerId.Value);
        }

        var where = filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : string.Empty;
        var orderBy = PositionsSortClauseBuilder.Build(query.Sort);
        var conn = await session.GetConnectionAsync(ct);

        var countSql = $"""
            SELECT count(*)
            FROM portfolio_positions p
            JOIN investment_accounts a ON a.id = p.account_id
            JOIN customers c           ON c.id = a.customer_id
            JOIN financial_products fp ON fp.id = p.product_id
            {where};
            """;
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(countSql, parameters, session.Transaction, cancellationToken: ct));

        parameters.Add("Limit", query.NormalizedPageSize);
        parameters.Add("Offset", query.Offset);
        var itemsSql = $"{BaseSelect} {where} ORDER BY {orderBy} LIMIT @Limit OFFSET @Offset;";
        var items = (await conn.QueryAsync<PositionDto>(new CommandDefinition(itemsSql, parameters, session.Transaction, cancellationToken: ct))).AsList();

        return (items, (int)total);
    }

    public async Task<IReadOnlyList<PositionDto>> GetByAccountAsync(Guid accountId, CancellationToken ct)
    {
        var conn = await session.GetConnectionAsync(ct);
        var sql = $"{BaseSelect} WHERE p.account_id = @accountId ORDER BY p.updated_at DESC;";
        var rows = await conn.QueryAsync<PositionDto>(new CommandDefinition(sql, new { accountId }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<PositionDto>> GetByCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var conn = await session.GetConnectionAsync(ct);
        var sql = $"{BaseSelect} WHERE c.id = @customerId ORDER BY p.updated_at DESC;";
        var rows = await conn.QueryAsync<PositionDto>(new CommandDefinition(sql, new { customerId }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
