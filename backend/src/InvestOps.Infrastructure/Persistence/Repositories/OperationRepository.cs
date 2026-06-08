using System.Data.Common;
using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class OperationRepository : IOperationRepository
{
    public Task<Operation?> GetByExternalIdAsync(DbConnection conn, string externalId, CancellationToken ct)
    {
        const string sql = "SELECT * FROM operations WHERE external_id = @externalId;";
        return conn.QuerySingleOrDefaultAsync<Operation>(new CommandDefinition(sql, new { externalId }, cancellationToken: ct));
    }

    public Task<Operation?> GetByIdAsync(DbConnection conn, Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM operations WHERE id = @id;";
        return conn.QuerySingleOrDefaultAsync<Operation>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task InsertAsync(DbConnection conn, DbTransaction tx, Operation op, CancellationToken ct)
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
        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, tx, cancellationToken: ct));
    }
}
