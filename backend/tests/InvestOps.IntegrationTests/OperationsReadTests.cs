using Dapper;
using InvestOps.Application.Operations.GetOperationById;
using InvestOps.Application.Operations.ListOperations;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class OperationsReadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private GetOperationByIdHandler GetByIdHandler() => new(new OperationRepository(_session));
    private ListOperationsHandler ListHandler() => new(new OperationRepository(_session));

    private async Task<(Guid AccountId, Guid ProductId)> SeedReferencesAsync()
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId, 'R', '0', 'Moderate', 'Active');", new { customerId });
        await conn.ExecuteAsync(
            "INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId, @customerId, 'ACC-R', 'Active');", new { accountId, customerId });
        await conn.ExecuteAsync(
            "INSERT INTO financial_products (id, product_type, name, risk_level, liquidity_days, is_active) VALUES (@productId, 'Stock', 'R', 3, 2, true);", new { productId });
        return (accountId, productId);
    }

    private async Task<Guid> InsertOperationAsync(Guid accountId, Guid productId, string status)
    {
        var id = Guid.NewGuid();
        await using var conn = await _factory.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            INSERT INTO operations
              (id, external_id, account_id, product_id, operation_type, quantity, unit_price,
               gross_value, net_value, operation_date, settlement_date, status, created_at, updated_at)
            VALUES
              (@id, @externalId, @accountId, @productId, 'Buy', 10, 5, 50, 50, @opDate, @setDate, @status, now(), now());
            """,
            new { id, externalId = $"EXT-{id:N}", accountId, productId, opDate = new DateOnly(2026, 6, 9), setDate = new DateOnly(2026, 6, 11), status });
        return id;
    }

    [Fact]
    public async Task GetById_returns_operation_when_exists()
    {
        var (accountId, productId) = await SeedReferencesAsync();
        var id = await InsertOperationAsync(accountId, productId, "Imported");

        var dto = await GetByIdHandler().HandleAsync(id, default);

        Assert.NotNull(dto);
        Assert.Equal(id, dto!.Id);
        Assert.Equal("Imported", dto.Status);
        Assert.Equal("Buy", dto.OperationType);
    }

    [Fact]
    public async Task GetById_returns_null_when_missing()
    {
        var dto = await GetByIdHandler().HandleAsync(Guid.NewGuid(), default);
        Assert.Null(dto);
    }

    [Fact]
    public async Task List_paginates_filtered_by_product()
    {
        var (accountId, productId) = await SeedReferencesAsync();
        for (var i = 0; i < 3; i++)
            await InsertOperationAsync(accountId, productId, "Imported");

        var page1 = await ListHandler().HandleAsync(new ListOperationsQuery(Page: 1, PageSize: 2, ProductId: productId), default);
        var page2 = await ListHandler().HandleAsync(new ListOperationsQuery(Page: 2, PageSize: 2, ProductId: productId), default);

        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(3, page2.Total);
        Assert.Single(page2.Items);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var (accountId, productId) = await SeedReferencesAsync();
        await InsertOperationAsync(accountId, productId, "Imported");
        await InsertOperationAsync(accountId, productId, "Processed");

        var processed = await ListHandler().HandleAsync(new ListOperationsQuery(ProductId: productId, Status: "Processed"), default);

        Assert.Equal(1, processed.Total);
        Assert.Equal("Processed", processed.Items[0].Status);
    }

    [Fact]
    public async Task List_returns_empty_for_invalid_status()
    {
        var result = await ListHandler().HandleAsync(new ListOperationsQuery(Status: "NotAStatus"), default);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }
}
