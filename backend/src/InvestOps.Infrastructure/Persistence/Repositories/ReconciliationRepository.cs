using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class ReconciliationRepository(IDbSession session) : IReconciliationRepository
{
    public async Task InsertImportAsync(ReconciliationImport import, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO reconciliation_imports (id, source, reference_date, status, created_at)
            VALUES (@Id, @Source, @ReferenceDate, @Status, @CreatedAt);
            """;
        var parameters = new
        {
            import.Id,
            import.Source,
            import.ReferenceDate,
            Status = import.Status.ToString(),
            import.CreatedAt
        };
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, session.Transaction, cancellationToken: ct));
    }

    public async Task InsertDivergencesAsync(IReadOnlyList<ReconciliationDivergence> divergences, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO reconciliation_divergences
              (id, reconciliation_import_id, account_id, product_id, internal_quantity, custodian_quantity, status, created_at, resolved_at)
            VALUES
              (@Id, @ReconciliationImportId, @AccountId, @ProductId, @InternalQuantity, @CustodianQuantity, @Status, @CreatedAt, @ResolvedAt);
            """;
        var conn = await session.GetConnectionAsync(ct);
        foreach (var d in divergences)
        {
            var parameters = new
            {
                d.Id,
                d.ReconciliationImportId,
                d.AccountId,
                d.ProductId,
                d.InternalQuantity,
                d.CustodianQuantity,
                Status = d.Status.ToString(),
                d.CreatedAt,
                d.ResolvedAt
            };
            await conn.ExecuteAsync(new CommandDefinition(sql, parameters, session.Transaction, cancellationToken: ct));
        }
    }

    public async Task<ReconciliationDivergence?> GetDivergenceByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, reconciliation_import_id, account_id, product_id, internal_quantity, custodian_quantity,
                   status, created_at, resolved_at
            FROM reconciliation_divergences
            WHERE id = @id;
            """;
        var conn = await session.GetConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<DivergenceRow>(
            new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
        if (row is null) return null;

        return new ReconciliationDivergence
        {
            Id = row.Id,
            ReconciliationImportId = row.ReconciliationImportId,
            AccountId = row.AccountId,
            ProductId = row.ProductId,
            InternalQuantity = row.InternalQuantity,
            CustodianQuantity = row.CustodianQuantity,
            Status = Enum.Parse<DivergenceStatus>(row.Status),
            CreatedAt = row.CreatedAt,
            ResolvedAt = row.ResolvedAt
        };
    }

    public async Task UpdateDivergenceStatusAsync(Guid id, string status, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        const string sql = "UPDATE reconciliation_divergences SET status = @status, resolved_at = @resolvedAt WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id, status, resolvedAt }, session.Transaction, cancellationToken: ct));
    }

    private sealed class DivergenceRow
    {
        public Guid Id { get; init; }
        public Guid ReconciliationImportId { get; init; }
        public Guid AccountId { get; init; }
        public Guid ProductId { get; init; }
        public decimal InternalQuantity { get; init; }
        public decimal CustodianQuantity { get; init; }
        public string Status { get; init; } = default!;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
    }
}
