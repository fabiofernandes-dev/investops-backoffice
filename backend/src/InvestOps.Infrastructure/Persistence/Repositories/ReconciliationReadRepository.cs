using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Application.Reconciliation.ListDivergences;
using InvestOps.Domain.Reconciliation;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class ReconciliationReadRepository(IDbSession session) : IReconciliationReadRepository
{
    private const string BaseJoin = """
        FROM reconciliation_divergences d
        JOIN investment_accounts a ON a.id = d.account_id
        JOIN customers c           ON c.id = a.customer_id
        JOIN financial_products fp ON fp.id = d.product_id
        """;

    public async Task<PagedResult<DivergenceDto>> ListDivergencesAsync(DivergencesQuery query, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var where = string.Empty;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            where = "WHERE d.status = @Status";
            parameters.Add("Status", query.Status);
        }

        var orderBy = DivergencesSortClauseBuilder.Build(query.Sort);
        var conn = await session.GetConnectionAsync(ct);

        var countSql = $"SELECT count(*) {BaseJoin} {where};";
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(countSql, parameters, session.Transaction, cancellationToken: ct));

        parameters.Add("Limit", query.NormalizedPageSize);
        parameters.Add("Offset", query.Offset);
        var itemsSql = $"""
            SELECT d.id, d.reconciliation_import_id AS import_id, d.account_id,
                   c.name AS customer_name, a.account_number, d.product_id, fp.name AS product_name, fp.ticker,
                   d.internal_quantity, d.custodian_quantity,
                   (d.custodian_quantity - d.internal_quantity) AS difference,
                   d.status, d.created_at, d.resolved_at
            {BaseJoin} {where} ORDER BY {orderBy} LIMIT @Limit OFFSET @Offset;
            """;
        var items = (await conn.QueryAsync<DivergenceDto>(new CommandDefinition(itemsSql, parameters, session.Transaction, cancellationToken: ct))).AsList();

        return new PagedResult<DivergenceDto>(items, (int)total, query.NormalizedPage, query.NormalizedPageSize);
    }

    public async Task<IReadOnlyList<InternalPosition>> GetInternalPositionsAsync(
        IReadOnlyList<Guid> accountIds, IReadOnlyList<Guid> productIds, CancellationToken ct)
    {
        const string sql = """
            SELECT account_id, product_id, quantity
            FROM portfolio_positions
            WHERE account_id = ANY(@accountIds) AND product_id = ANY(@productIds);
            """;
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<InternalPosition>(
            new CommandDefinition(sql, new { accountIds = accountIds.ToArray(), productIds = productIds.ToArray() }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Guid>> GetExistingAccountIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        const string sql = "SELECT id FROM investment_accounts WHERE id = ANY(@ids);";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(sql, new { ids = ids.ToArray() }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Guid>> GetExistingProductIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        const string sql = "SELECT id FROM financial_products WHERE id = ANY(@ids);";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(sql, new { ids = ids.ToArray() }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
