using Dapper;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Application.Operations.ReprocessOperation;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ReprocessOperationTests(PostgresFixture fixture)
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);

    private static ReprocessOperationHandler BuildHandler(IDbSession session) => new(
        new UnitOfWork(session),
        new OperationRepository(session),
        new OutboxRepository(session),
        new AuditRepository(session),
        TimeProvider.System);

    private async Task<Guid> SeedImportedOperationAsync()
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId,'T','0','Moderate','Active');", new { customerId });
        await conn.ExecuteAsync("INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId,@customerId,'ACC','Active');", new { accountId, customerId });
        await conn.ExecuteAsync("INSERT INTO financial_products (id, product_type, name, risk_level, liquidity_days, is_active) VALUES (@productId,'Stock','P',1,2,true);", new { productId });
        await conn.ExecuteAsync("""
            INSERT INTO operations (id, external_id, account_id, product_id, operation_type, quantity, unit_price, gross_value, net_value, operation_date, settlement_date, status, created_at, updated_at)
            VALUES (@id, @ext, @accountId, @productId, 'Buy', 10, 5, 50, 50, '2026-06-13', '2026-06-15', 'Imported', now(), now());
            """, new { id, ext = $"EXT-{id:N}", accountId, productId });
        return id;
    }

    [Fact]
    public async Task Reprocess_republishes_operation_imported_event()
    {
        var id = await SeedImportedOperationAsync();

        await using (var session = new DbSession(_factory))
            await BuildHandler(session).HandleAsync(id, Guid.NewGuid(), default);

        await using var conn = await _factory.OpenConnectionAsync();
        var outboxCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM outbox_messages WHERE event_type='OperationImported' AND source='api' AND payload->'payload'->>'operationId' = @id;",
            new { id = id.ToString() });
        Assert.Equal(1L, outboxCount);

        var auditCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM audit_logs WHERE entity_id=@id AND action='Reprocess';", new { id = id.ToString() });
        Assert.Equal(1L, auditCount);
    }

    [Fact]
    public async Task Reprocess_throws_when_operation_missing()
    {
        await using var session = new DbSession(_factory);
        await Assert.ThrowsAsync<ReferenceNotFoundException>(
            () => BuildHandler(session).HandleAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }
}
