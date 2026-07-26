using Dapper;
using InvestOps.Application.Dashboard;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class DashboardReadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private GetDashboardSummaryHandler SummaryHandler() => new(new DashboardRepository(_session));
    private GetProcessingMetricsHandler MetricsHandler() => new(new DashboardRepository(_session));

    // O dashboard agrega globalmente; a Postgres é compartilhada na collection.
    // Limpa o que é contado para isolar o cenário (FK: processing_events antes de operations).
    private async Task ResetAsync()
    {
        await using var conn = await _factory.OpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM processing_events; DELETE FROM operations;");
    }

    private async Task<(Guid AccountId, Guid ProductId)> SeedReferencesAsync()
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId,'T','0','Moderate','Active');", new { customerId });
        await conn.ExecuteAsync("INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId,@customerId,'ACC','Active');", new { accountId, customerId });
        await conn.ExecuteAsync("INSERT INTO financial_products (id, product_type, name, risk_level, liquidity_days, is_active) VALUES (@productId,'Stock','P',3,2,true);", new { productId });
        return (accountId, productId);
    }

    private async Task InsertOperationAsync(Guid accountId, Guid productId, string status)
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("""
            INSERT INTO operations (id, external_id, account_id, product_id, operation_type, quantity, unit_price, gross_value, net_value, operation_date, settlement_date, status, created_at, updated_at)
            VALUES (@id, @ext, @accountId, @productId, 'Buy', 10, 5, 50, 50, '2026-06-13', '2026-06-15', @status, now(), now());
            """, new { id, ext = $"EXT-{id:N}", accountId, productId, status });
        await conn.ExecuteAsync("""
            INSERT INTO processing_events (id, operation_id, event_type, status, attempt_count, created_at, processed_at)
            VALUES (@peid, @id, 'OperationProcessed', @status, 0, now() - interval '2 seconds', now());
            """, new { peid = Guid.NewGuid(), id, status });
    }

    [Fact]
    public async Task Summary_counts_by_status_and_average()
    {
        await ResetAsync();
        var (accountId, productId) = await SeedReferencesAsync();
        await InsertOperationAsync(accountId, productId, "Processed");
        await InsertOperationAsync(accountId, productId, "Processed");
        await InsertOperationAsync(accountId, productId, "Rejected");

        var summary = await SummaryHandler().HandleAsync(default);

        Assert.Equal(3, summary.ImportedToday);
        Assert.Equal(2, summary.Processed);
        Assert.Equal(1, summary.Rejected);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.DivergencesPending);
        Assert.NotNull(summary.AverageProcessingSeconds);
        Assert.True(summary.AverageProcessingSeconds > 0);
    }

    [Fact]
    public async Task ProcessingMetrics_groups_by_status()
    {
        await ResetAsync();
        var (accountId, productId) = await SeedReferencesAsync();
        await InsertOperationAsync(accountId, productId, "Processed");
        await InsertOperationAsync(accountId, productId, "Rejected");

        var metrics = await MetricsHandler().HandleAsync(default);

        Assert.Equal(1, metrics.OperationsByStatus["Processed"]);
        Assert.Equal(1, metrics.OperationsByStatus["Rejected"]);
    }
}
