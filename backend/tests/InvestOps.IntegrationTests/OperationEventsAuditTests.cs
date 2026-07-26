using Dapper;
using InvestOps.Application.Operations.GetOperationAudit;
using InvestOps.Application.Operations.GetOperationEvents;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class OperationEventsAuditTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private GetOperationEventsHandler EventsHandler() => new(new ProcessingEventReadRepository(_session));
    private GetOperationAuditHandler AuditHandler() => new(new AuditRepository(_session));

    private async Task<Guid> SeedOperationWithEventAndAuditAsync()
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        await conn.ExecuteAsync("INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId,'T','0','Moderate','Active');", new { customerId });
        await conn.ExecuteAsync("INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId,@customerId,'ACC','Active');", new { accountId, customerId });
        await conn.ExecuteAsync("INSERT INTO financial_products (id, product_type, name, risk_level, liquidity_days, is_active) VALUES (@productId,'Stock','P',3,2,true);", new { productId });
        await conn.ExecuteAsync("""
            INSERT INTO operations (id, external_id, account_id, product_id, operation_type, quantity, unit_price, gross_value, net_value, operation_date, settlement_date, status, created_at, updated_at)
            VALUES (@operationId, @ext, @accountId, @productId, 'Buy', 10, 5, 50, 50, '2026-06-13', '2026-06-15', 'Processed', now(), now());
            """, new { operationId, ext = $"EXT-{operationId:N}", accountId, productId });
        await conn.ExecuteAsync("""
            INSERT INTO processing_events (id, operation_id, event_type, status, attempt_count, created_at, processed_at)
            VALUES (@id, @operationId, 'OperationProcessed', 'Processed', 0, now(), now());
            """, new { id = Guid.NewGuid(), operationId });
        await conn.ExecuteAsync("""
            INSERT INTO audit_logs (id, entity_name, entity_id, action, new_value, created_at)
            VALUES (@id, 'Operation', @entityId, 'Imported', '{"status":"Imported"}'::jsonb, now());
            """, new { id = Guid.NewGuid(), entityId = operationId.ToString() });

        return operationId;
    }

    [Fact]
    public async Task Events_returns_processing_events_of_operation()
    {
        var operationId = await SeedOperationWithEventAndAuditAsync();

        var events = await EventsHandler().HandleAsync(operationId, default);

        Assert.Single(events);
        Assert.Equal("OperationProcessed", events[0].EventType);
        Assert.Equal("Processed", events[0].Status);
    }

    [Fact]
    public async Task Audit_returns_audit_logs_of_operation()
    {
        var operationId = await SeedOperationWithEventAndAuditAsync();

        var audit = await AuditHandler().HandleAsync(operationId, default);

        Assert.Single(audit);
        Assert.Equal("Imported", audit[0].Action);
        Assert.Contains("Imported", audit[0].NewValue);
    }

    [Fact]
    public async Task Events_and_audit_return_empty_for_unknown_operation()
    {
        var unknown = Guid.NewGuid();
        Assert.Empty(await EventsHandler().HandleAsync(unknown, default));
        Assert.Empty(await AuditHandler().HandleAsync(unknown, default));
    }
}
