using System.Data.Common;
using Dapper;
using InvestOps.Application.Operations.ListProcessingEvents;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ProcessingEventsReadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private ListProcessingEventsHandler ListHandler() => new(new ProcessingEventReadRepository(_session));

    private async Task<(Guid AccountId, Guid ProductId)> SeedReferencesAsync(DbConnection conn)
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId, 'PE Cliente', '0', 'Moderate', 'Active');", new { customerId });
        await conn.ExecuteAsync(
            "INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId, @customerId, 'ACC-PE', 'Active');", new { accountId, customerId });
        await conn.ExecuteAsync(
            "INSERT INTO financial_products (id, product_type, name, risk_level, liquidity_days, is_active) VALUES (@productId, 'Stock', 'PE', 3, 2, true);", new { productId });
        return (accountId, productId);
    }

    private async Task<(Guid OperationId, string ExternalId)> SeedOperationAsync(DbConnection conn, Guid accountId, Guid productId)
    {
        var id = Guid.NewGuid();
        var externalId = $"EXT-{id:N}";
        await conn.ExecuteAsync("""
            INSERT INTO operations
              (id, external_id, account_id, product_id, operation_type, quantity, unit_price,
               gross_value, net_value, operation_date, settlement_date, status, created_at, updated_at)
            VALUES
              (@id, @externalId, @accountId, @productId, 'Buy', 10, 5, 50, 50, @opDate, @setDate, 'Processed', now(), now());
            """,
            new { id, externalId, accountId, productId, opDate = new DateOnly(2026, 7, 1), setDate = new DateOnly(2026, 7, 3) });
        return (id, externalId);
    }

    private static Task SeedProcessingEventAsync(
        DbConnection conn, Guid operationId, string eventType, string status,
        int attemptCount = 0, string? errorMessage = null, bool processed = true) =>
        conn.ExecuteAsync("""
            INSERT INTO processing_events
              (id, operation_id, event_type, payload, status, attempt_count, error_message, created_at, processed_at)
            VALUES
              (@id, @operationId, @eventType, '{}'::jsonb, @status, @attemptCount, @errorMessage, now(), @processedAt);
            """,
            new
            {
                id = Guid.NewGuid(),
                operationId,
                eventType,
                status,
                attemptCount,
                errorMessage,
                processedAt = processed ? DateTime.UtcNow : (DateTime?)null
            });

    [Fact]
    public async Task List_joins_external_id_and_paginates()
    {
        Guid operationId;
        string externalId;
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn);
            (operationId, externalId) = await SeedOperationAsync(conn, accountId, productId);
            for (var i = 0; i < 3; i++)
                await SeedProcessingEventAsync(conn, operationId, "OperationProcessed", "Processed");
        }

        var page1 = await ListHandler().HandleAsync(new ProcessingEventsQuery(Page: 1, PageSize: 2, Status: "Processed"), default);
        var page2 = await ListHandler().HandleAsync(new ProcessingEventsQuery(Page: 2, PageSize: 2, Status: "Processed"), default);

        Assert.True(page1.Total >= 3);
        Assert.Equal(2, page1.Items.Count);
        Assert.NotEmpty(page2.Items);
        Assert.All(page1.Items, e => Assert.Equal("Processed", e.Status));
        Assert.All(page1.Items, e => Assert.False(string.IsNullOrEmpty(e.ExternalId)));

        var scoped = await ListHandler().HandleAsync(new ProcessingEventsQuery(PageSize: 100, Status: "Processed"), default);
        var mine = scoped.Items.Where(e => e.OperationId == operationId).ToList();
        Assert.Equal(3, mine.Count);
        Assert.All(mine, e =>
        {
            Assert.Equal(externalId, e.ExternalId);
            Assert.Equal("OperationProcessed", e.EventType);
        });
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        Guid operationId;
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn);
            (operationId, _) = await SeedOperationAsync(conn, accountId, productId);
            await SeedProcessingEventAsync(conn, operationId, "OperationProcessed", "Processed");
            await SeedProcessingEventAsync(conn, operationId, "OperationRejected", "Rejected");
            await SeedProcessingEventAsync(conn, operationId, "OperationFailed", "Failed", attemptCount: 3, errorMessage: "boom");
        }

        var rejected = await ListHandler().HandleAsync(new ProcessingEventsQuery(PageSize: 50, Status: "Rejected"), default);

        Assert.All(rejected.Items, e => Assert.Equal("Rejected", e.Status));
        Assert.Single(rejected.Items, e => e.OperationId == operationId);
    }

    [Fact]
    public async Task List_failed_row_exposes_attempt_count_and_error_message()
    {
        Guid operationId;
        string externalId;
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn);
            (operationId, externalId) = await SeedOperationAsync(conn, accountId, productId);
            await SeedProcessingEventAsync(conn, operationId, "OperationFailed", "Failed", attemptCount: 5, errorMessage: "timeout", processed: true);
        }

        var failed = await ListHandler().HandleAsync(new ProcessingEventsQuery(PageSize: 50, Status: "Failed"), default);

        var mine = Assert.Single(failed.Items, e => e.OperationId == operationId);
        Assert.Equal(externalId, mine.ExternalId);
        Assert.Equal("OperationFailed", mine.EventType);
        Assert.Equal(5, mine.AttemptCount);
        Assert.Equal("timeout", mine.ErrorMessage);
        Assert.NotNull(mine.ProcessedAt);
    }

    [Fact]
    public async Task List_orders_by_attempt_count_ascending()
    {
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            var (accountId, productId) = await SeedReferencesAsync(conn);
            var (operationId, _) = await SeedOperationAsync(conn, accountId, productId);
            await SeedProcessingEventAsync(conn, operationId, "OperationFailed", "Failed", attemptCount: 4, errorMessage: "b");
            await SeedProcessingEventAsync(conn, operationId, "OperationFailed", "Failed", attemptCount: 1, errorMessage: "a");
        }

        var result = await ListHandler().HandleAsync(new ProcessingEventsQuery(PageSize: 50, Status: "Failed", Sort: "attemptCount:asc"), default);

        var attempts = result.Items.Select(e => e.AttemptCount).ToList();
        var sorted = attempts.OrderBy(a => a).ToList();
        Assert.Equal(sorted, attempts);
    }
}
