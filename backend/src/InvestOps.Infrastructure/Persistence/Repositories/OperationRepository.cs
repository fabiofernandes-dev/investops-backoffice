using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.ListOperations;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class OperationRepository(IDbSession session) : IOperationRepository
{
    public async Task<Operation?> GetByExternalIdAsync(string externalId, CancellationToken ct)
    {
        const string sql = "SELECT * FROM operations WHERE external_id = @externalId;";
        var conn = await session.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Operation>(
            new CommandDefinition(sql, new { externalId }, session.Transaction, cancellationToken: ct));
    }

    public async Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM operations WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Operation>(
            new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
    }

    public async Task InsertAsync(Operation op, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO operations
              (id, external_id, account_id, product_id, operation_type, quantity, unit_price,
               gross_value, net_value, operation_date, settlement_date, status, rejection_reason,
               created_at, updated_at)
            VALUES
              (@Id, @ExternalId, @AccountId, @ProductId, @OperationType, @Quantity, @UnitPrice,
               @GrossValue, @NetValue, @OperationDate, @SettlementDate, @Status, @RejectionReason,
               @CreatedAt, @UpdatedAt);
            """;
        var parameters = new
        {
            op.Id,
            op.ExternalId,
            op.AccountId,
            op.ProductId,
            OperationType = op.OperationType.ToString(),
            op.Quantity,
            op.UnitPrice,
            op.GrossValue,
            op.NetValue,
            op.OperationDate,
            op.SettlementDate,
            Status = op.Status.ToString(),
            RejectionReason = op.RejectionReason?.ToString(),
            op.CreatedAt,
            op.UpdatedAt
        };
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, session.Transaction, cancellationToken: ct));
    }

    private const string ListJoin = """
        FROM operations o
        JOIN investment_accounts a ON a.id = o.account_id
        JOIN customers c           ON c.id = a.customer_id
        JOIN financial_products fp ON fp.id = o.product_id
        """;

    public async Task<(IReadOnlyList<OperationListItemDto> Items, int Total)> ListAsync(ListOperationsQuery query, CancellationToken ct)
    {
        var filters = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filters.Add("o.status = @Status");
            parameters.Add("Status", query.Status);
        }
        if (query.ProductId.HasValue)
        {
            filters.Add("o.product_id = @ProductId");
            parameters.Add("ProductId", query.ProductId.Value);
        }
        if (query.CustomerId.HasValue)
        {
            filters.Add("a.customer_id = @CustomerId");
            parameters.Add("CustomerId", query.CustomerId.Value);
        }

        var where = filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : string.Empty;
        var orderBy = SortClauseBuilder.Build(query.Sort); // coluna validada por whitelist (anti-injection), qualificada com o.

        var conn = await session.GetConnectionAsync(ct);

        var countSql = $"SELECT count(*) {ListJoin} {where};";
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(countSql, parameters, session.Transaction, cancellationToken: ct));

        parameters.Add("Limit", query.NormalizedPageSize);
        parameters.Add("Offset", query.Offset);
        var itemsSql = $"""
            SELECT o.id, o.external_id, o.account_id, o.product_id,
                   c.name AS customer_name, a.account_number, fp.name AS product_name, fp.ticker,
                   o.operation_type, o.quantity, o.unit_price, o.gross_value, o.net_value,
                   o.operation_date, o.settlement_date, o.status, o.rejection_reason
            {ListJoin} {where} ORDER BY {orderBy} LIMIT @Limit OFFSET @Offset;
            """;
        var items = (await conn.QueryAsync<OperationListItemDto>(new CommandDefinition(itemsSql, parameters, session.Transaction, cancellationToken: ct))).AsList();

        return (items, (int)total);
    }
}
