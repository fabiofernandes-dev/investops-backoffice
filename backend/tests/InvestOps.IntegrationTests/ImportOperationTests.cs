using Dapper;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ImportOperationTests(PostgresFixture fixture)
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);

    private static ImportOperationHandler BuildHandler(IDbSession session) => new(
        new UnitOfWork(session),
        new OperationRepository(session),
        new FinancialProductRepository(session),
        new InvestmentAccountRepository(session),
        new OutboxRepository(session),
        new AuditRepository(session),
        TimeProvider.System);

    private async Task<(Guid AccountId, Guid ProductId)> SeedReferencesAsync()
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId, 'Test', '00000000000', 'Moderate', 'Active');",
            new { customerId });
        await conn.ExecuteAsync(
            "INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId, @customerId, 'ACC-T', 'Active');",
            new { accountId, customerId });
        await conn.ExecuteAsync(
            "INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active) VALUES (@productId, 'Stock', 'Test', 'TST', 3, 2, true);",
            new { productId });

        return (accountId, productId);
    }

    private static ImportOperationRequest Request(Guid accountId, Guid productId, string externalId) =>
        new(externalId, accountId, productId, "Buy", 100m, 25.40m, new DateOnly(2026, 6, 7));

    [Fact]
    public async Task Import_persists_operation_outbox_and_audit_in_one_transaction()
    {
        var (accountId, productId) = await SeedReferencesAsync();
        var externalId = $"EXT-{Guid.NewGuid():N}";

        Guid resultId;
        await using (var session = new DbSession(_factory))
        {
            var result = await BuildHandler(session).HandleAsync(Request(accountId, productId, externalId), Guid.NewGuid(), default);
            Assert.True(result.Created);
            Assert.Equal("Imported", result.Status);
            resultId = result.Id;
        }

        await using var conn = await _factory.OpenConnectionAsync();
        var op = await conn.QuerySingleAsync<(string Status, decimal GrossValue, DateOnly SettlementDate)>(
            "SELECT status AS \"Status\", gross_value AS \"GrossValue\", settlement_date AS \"SettlementDate\" FROM operations WHERE external_id = @externalId;",
            new { externalId });
        Assert.Equal("Imported", op.Status);
        Assert.Equal(2540.00m, op.GrossValue);
        Assert.Equal(new DateOnly(2026, 6, 9), op.SettlementDate); // 2026-06-07 + 2 dias

        // payload é o envelope serializado em PascalCase (System.Text.Json default): Payload.OperationId
        var outboxCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM outbox_messages WHERE event_type='OperationImported' AND status='Pending' AND payload->'Payload'->>'OperationId' = @id;",
            new { id = resultId.ToString() });
        Assert.Equal(1L, outboxCount);

        var auditCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM audit_logs WHERE entity_id = @id;", new { id = resultId.ToString() });
        Assert.Equal(1L, auditCount);
    }

    [Fact]
    public async Task Import_is_idempotent_on_external_id()
    {
        var (accountId, productId) = await SeedReferencesAsync();
        var externalId = $"EXT-{Guid.NewGuid():N}";

        await using var session = new DbSession(_factory);
        var handler = BuildHandler(session);

        var first = await handler.HandleAsync(Request(accountId, productId, externalId), Guid.NewGuid(), default);
        var second = await handler.HandleAsync(Request(accountId, productId, externalId), Guid.NewGuid(), default);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Id, second.Id);

        await using var conn = await _factory.OpenConnectionAsync();
        var opCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM operations WHERE external_id = @externalId;", new { externalId });
        Assert.Equal(1L, opCount);
    }

    [Fact]
    public async Task Import_throws_when_product_missing()
    {
        var (accountId, _) = await SeedReferencesAsync();
        var request = Request(accountId, Guid.NewGuid(), $"EXT-{Guid.NewGuid():N}");

        await using var session = new DbSession(_factory);
        await Assert.ThrowsAsync<ReferenceNotFoundException>(
            () => BuildHandler(session).HandleAsync(request, Guid.NewGuid(), default));
    }
}
