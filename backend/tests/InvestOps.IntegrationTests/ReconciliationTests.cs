using System.Data.Common;
using Dapper;
using InvestOps.Application.Common;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Application.Reconciliation.IgnoreDivergence;
using InvestOps.Application.Reconciliation.ImportReconciliation;
using InvestOps.Application.Reconciliation.ListDivergences;
using InvestOps.Application.Reconciliation.ResolveDivergence;
using InvestOps.Domain.Reconciliation;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ReconciliationTests(PostgresFixture fixture)
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);

    private static ImportReconciliationHandler BuildImportHandler(IDbSession session) => new(
        new UnitOfWork(session),
        new ReconciliationRepository(session),
        new ReconciliationReadRepository(session),
        new AuditRepository(session),
        TimeProvider.System);

    private static ResolveDivergenceHandler BuildResolveHandler(IDbSession session) => new(
        new UnitOfWork(session),
        new ReconciliationRepository(session),
        new AuditRepository(session),
        TimeProvider.System);

    private static IgnoreDivergenceHandler BuildIgnoreHandler(IDbSession session) => new(
        new UnitOfWork(session),
        new ReconciliationRepository(session),
        new AuditRepository(session),
        TimeProvider.System);

    private async Task<(Guid AccountId, Guid ProductId)> SeedReferencesAsync(
        DbConnection conn, string customerName, decimal? positionQty = null)
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId, @customerName, @doc, 'Moderate', 'Active');",
            new { customerId, customerName, doc = Guid.NewGuid().ToString("N")[..11] });
        await conn.ExecuteAsync(
            "INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId, @customerId, @accNum, 'Active');",
            new { accountId, customerId, accNum = "ACC-" + Guid.NewGuid().ToString("N")[..6] });
        await conn.ExecuteAsync(
            "INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active) VALUES (@productId, 'Stock', @name, @ticker, 3, 2, true);",
            new { productId, name = customerName + " Produto", ticker = "T" + Guid.NewGuid().ToString("N")[..5] });

        if (positionQty.HasValue)
            await conn.ExecuteAsync(
                "INSERT INTO portfolio_positions (id, account_id, product_id, quantity, average_price, gross_amount, updated_at) VALUES (@id, @accountId, @productId, @qty, 10, 1000, now());",
                new { id = Guid.NewGuid(), accountId, productId, qty = positionQty.Value });

        return (accountId, productId);
    }

    private static async Task<Guid> SeedImportAsync(DbConnection conn, string source)
    {
        var importId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO reconciliation_imports (id, source, reference_date, status, created_at) VALUES (@importId, @source, @refDate, 'Completed', now());",
            new { importId, source, refDate = new DateOnly(2026, 7, 1) });
        return importId;
    }

    private static Task SeedDivergenceAsync(
        DbConnection conn, Guid id, Guid importId, Guid accountId, Guid productId,
        decimal internalQ, decimal custodianQ, string status) =>
        conn.ExecuteAsync(
            """
            INSERT INTO reconciliation_divergences
              (id, reconciliation_import_id, account_id, product_id, internal_quantity, custodian_quantity, status, created_at, resolved_at)
            VALUES (@id, @importId, @accountId, @productId, @internalQ, @custodianQ, @status, now(), null);
            """,
            new { id, importId, accountId, productId, internalQ, custodianQ, status });

    [Fact]
    public async Task Import_persists_import_divergences_and_audit()
    {
        var source = $"custodian-{Guid.NewGuid():N}";
        Guid accountId, productMatch, productDiverge;
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            (accountId, productDiverge) = await SeedReferencesAsync(conn, "Import Cliente", positionQty: 100m);
            // second product on the same account, internal position equal to the custodian row (matches)
            productMatch = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active) VALUES (@productMatch, 'Stock', 'Match', @ticker, 3, 2, true);",
                new { productMatch, ticker = "M" + Guid.NewGuid().ToString("N")[..5] });
            await conn.ExecuteAsync(
                "INSERT INTO portfolio_positions (id, account_id, product_id, quantity, average_price, gross_amount, updated_at) VALUES (@id, @accountId, @productMatch, 100, 10, 1000, now());",
                new { id = Guid.NewGuid(), accountId, productMatch });
        }

        var rows = new[]
        {
            new CustodianRow(accountId, productDiverge, 150m), // internal 100 -> diverges
            new CustodianRow(accountId, productMatch, 100m)    // internal 100 -> matches
        };
        var request = new ImportReconciliationRequest(source, new DateOnly(2026, 7, 1), rows);

        ImportReconciliationResult result;
        await using (var session = new DbSession(_factory))
            result = await BuildImportHandler(session).HandleAsync(request, default);

        Assert.Equal(1, result.DivergenceCount);
        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(2, result.RowsProcessed);

        await using var verify = await _factory.OpenConnectionAsync();
        var importRow = await verify.QuerySingleAsync<(string Status, Guid Id)>(
            "SELECT status AS \"Status\", id AS \"Id\" FROM reconciliation_imports WHERE source = @source;", new { source });
        Assert.Equal("Completed", importRow.Status);
        Assert.Equal(result.ImportId, importRow.Id);

        var div = await verify.QuerySingleAsync<(string Status, decimal Internal, decimal Custodian, Guid ProductId)>(
            "SELECT status AS \"Status\", internal_quantity AS \"Internal\", custodian_quantity AS \"Custodian\", product_id AS \"ProductId\" FROM reconciliation_divergences WHERE reconciliation_import_id = @importId;",
            new { importId = result.ImportId });
        Assert.Equal("Pending", div.Status);
        Assert.Equal(100m, div.Internal);
        Assert.Equal(150m, div.Custodian);
        Assert.Equal(productDiverge, div.ProductId);

        var auditCount = await verify.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM audit_logs WHERE entity_name = 'ReconciliationImport' AND entity_id = @id AND action = 'Imported';",
            new { id = result.ImportId.ToString() });
        Assert.Equal(1L, auditCount);
    }

    [Fact]
    public async Task Import_is_atomic_when_reference_missing()
    {
        var source = $"custodian-{Guid.NewGuid():N}";
        Guid accountId, productId;
        await using (var conn = await _factory.OpenConnectionAsync())
            (accountId, productId) = await SeedReferencesAsync(conn, "Atomic Cliente", positionQty: 100m);

        var rows = new[]
        {
            new CustodianRow(accountId, productId, 150m),
            new CustodianRow(accountId, Guid.NewGuid(), 50m) // produto inexistente
        };
        var request = new ImportReconciliationRequest(source, new DateOnly(2026, 7, 1), rows);

        await using (var session = new DbSession(_factory))
            await Assert.ThrowsAsync<ReferenceNotFoundException>(() => BuildImportHandler(session).HandleAsync(request, default));

        await using var verify = await _factory.OpenConnectionAsync();
        var importCount = await verify.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM reconciliation_imports WHERE source = @source;", new { source });
        var divergenceCount = await verify.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM reconciliation_divergences WHERE account_id = @accountId;", new { accountId });
        var auditCount = await verify.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM audit_logs WHERE entity_name = 'ReconciliationImport' AND new_value->>'Source' = @source;", new { source });

        Assert.Equal(0L, importCount);
        Assert.Equal(0L, divergenceCount);
        Assert.Equal(0L, auditCount);
    }

    [Fact]
    public async Task List_joins_names_paginates_and_filters_by_status()
    {
        Guid accountId, productId, importId;
        var resolvedId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            (accountId, productId) = await SeedReferencesAsync(conn, "List Cliente");
            importId = await SeedImportAsync(conn, $"custodian-{Guid.NewGuid():N}");
            for (var i = 0; i < 3; i++)
                await SeedDivergenceAsync(conn, Guid.NewGuid(), importId, accountId, productId, 10, 20 + i, "Pending");
            await SeedDivergenceAsync(conn, resolvedId, importId, accountId, productId, 5, 9, "Resolved");
        }

        await using var session = new DbSession(_factory);
        var handler = new ListDivergencesHandler(new ReconciliationReadRepository(session));

        var pending = await handler.HandleAsync(new DivergencesQuery(Page: 1, PageSize: 2, Status: "Pending"), default);
        Assert.Equal(2, pending.Items.Count);
        Assert.True(pending.Total >= 3);
        Assert.All(pending.Items, d => Assert.Equal("Pending", d.Status));

        var resolved = await handler.HandleAsync(new DivergencesQuery(Page: 1, PageSize: 50, Status: "Resolved"), default);
        Assert.All(resolved.Items, d => Assert.Equal("Resolved", d.Status));
        var mine = Assert.Single(resolved.Items, d => d.Id == resolvedId);
        Assert.Equal("List Cliente", mine.CustomerName);
        Assert.StartsWith("List Cliente", mine.ProductName);
        Assert.Equal(4m, mine.Difference); // 9 - 5
    }

    [Fact]
    public async Task Resolve_transitions_pending_to_resolved_with_audit()
    {
        var divergenceId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn, "Resolve Cliente");
            var importId = await SeedImportAsync(conn, $"custodian-{Guid.NewGuid():N}");
            await SeedDivergenceAsync(conn, divergenceId, importId, accountId, productId, 10, 20, "Pending");
        }

        await using (var session = new DbSession(_factory))
            await BuildResolveHandler(session).HandleAsync(divergenceId, default);

        await using var verify = await _factory.OpenConnectionAsync();
        var row = await verify.QuerySingleAsync<(string Status, DateTimeOffset? ResolvedAt)>(
            "SELECT status AS \"Status\", resolved_at AS \"ResolvedAt\" FROM reconciliation_divergences WHERE id = @id;", new { id = divergenceId });
        Assert.Equal("Resolved", row.Status);
        Assert.NotNull(row.ResolvedAt);

        var auditCount = await verify.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM audit_logs WHERE entity_name = 'ReconciliationDivergence' AND entity_id = @id AND action = 'Resolved';",
            new { id = divergenceId.ToString() });
        Assert.Equal(1L, auditCount);
    }

    [Fact]
    public async Task Ignore_transitions_pending_to_ignored_with_audit()
    {
        var divergenceId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn, "Ignore Cliente");
            var importId = await SeedImportAsync(conn, $"custodian-{Guid.NewGuid():N}");
            await SeedDivergenceAsync(conn, divergenceId, importId, accountId, productId, 10, 20, "Pending");
        }

        await using (var session = new DbSession(_factory))
            await BuildIgnoreHandler(session).HandleAsync(divergenceId, default);

        await using var verify = await _factory.OpenConnectionAsync();
        var status = await verify.ExecuteScalarAsync<string>(
            "SELECT status FROM reconciliation_divergences WHERE id = @id;", new { id = divergenceId });
        Assert.Equal("Ignored", status);

        var auditCount = await verify.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM audit_logs WHERE entity_name = 'ReconciliationDivergence' AND entity_id = @id AND action = 'Ignored';",
            new { id = divergenceId.ToString() });
        Assert.Equal(1L, auditCount);
    }

    [Fact]
    public async Task Resolve_throws_conflict_when_not_pending()
    {
        var divergenceId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn, "Conflict Cliente");
            var importId = await SeedImportAsync(conn, $"custodian-{Guid.NewGuid():N}");
            await SeedDivergenceAsync(conn, divergenceId, importId, accountId, productId, 10, 20, "Resolved");
        }

        await using var session = new DbSession(_factory);
        await Assert.ThrowsAsync<ConflictException>(() => BuildResolveHandler(session).HandleAsync(divergenceId, default));
    }

    [Fact]
    public async Task Resolve_throws_not_found_for_unknown_id()
    {
        await using var session = new DbSession(_factory);
        await Assert.ThrowsAsync<ReferenceNotFoundException>(() => BuildResolveHandler(session).HandleAsync(Guid.NewGuid(), default));
    }
}
