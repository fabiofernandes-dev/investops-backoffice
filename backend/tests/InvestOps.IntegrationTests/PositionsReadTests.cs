using Dapper;
using InvestOps.Application.Positions;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class PositionsReadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private ListPositionsHandler ListHandler() => new(new PositionReadRepository(_session));
    private GetPositionsByAccountHandler ByAccountHandler() => new(new PositionReadRepository(_session));
    private GetPositionsByCustomerHandler ByCustomerHandler() => new(new PositionReadRepository(_session));

    private async Task<(Guid CustomerId, Guid AccountId, Guid[] ProductIds)> SeedAsync(int positions)
    {
        await using var conn = await _factory.OpenConnectionAsync();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId,'Cliente X','0','Moderate','Active');", new { customerId });
        await conn.ExecuteAsync("INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId,@customerId,'ACC-1','Active');", new { accountId, customerId });

        var productIds = new Guid[positions];
        for (var i = 0; i < positions; i++)
        {
            var productId = Guid.NewGuid();
            productIds[i] = productId;
            await conn.ExecuteAsync("INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active) VALUES (@productId,'Stock',@name,@ticker,3,2,true);",
                new { productId, name = $"Produto {i}", ticker = $"TICK{i}" });
            await conn.ExecuteAsync("INSERT INTO portfolio_positions (id, account_id, product_id, quantity, average_price, gross_amount, updated_at) VALUES (@id,@accountId,@productId,100,10,1000,now());",
                new { id = Guid.NewGuid(), accountId, productId });
        }
        return (customerId, accountId, productIds);
    }

    [Fact]
    public async Task List_returns_positions_with_names_and_paginates()
    {
        var (_, accountId, _) = await SeedAsync(3);

        var page1 = await ListHandler().HandleAsync(new PositionsQuery(Page: 1, PageSize: 2, AccountId: accountId), default);
        var page2 = await ListHandler().HandleAsync(new PositionsQuery(Page: 2, PageSize: 2, AccountId: accountId), default);

        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal("Cliente X", page1.Items[0].CustomerName);
        Assert.Equal("ACC-1", page1.Items[0].AccountNumber);
        Assert.StartsWith("Produto", page1.Items[0].ProductName);
    }

    [Fact]
    public async Task List_filters_by_product()
    {
        var (_, accountId, productIds) = await SeedAsync(3);

        var result = await ListHandler().HandleAsync(new PositionsQuery(AccountId: accountId, ProductId: productIds[0]), default);

        Assert.Equal(1, result.Total);
        Assert.Equal(productIds[0], result.Items[0].ProductId);
    }

    [Fact]
    public async Task ByAccount_and_ByCustomer_return_positions()
    {
        var (customerId, accountId, _) = await SeedAsync(2);

        var byAccount = await ByAccountHandler().HandleAsync(accountId, default);
        var byCustomer = await ByCustomerHandler().HandleAsync(customerId, default);

        Assert.Equal(2, byAccount.Count);
        Assert.Equal(2, byCustomer.Count);
    }

    [Fact]
    public async Task ByAccount_returns_empty_for_unknown()
    {
        var result = await ByAccountHandler().HandleAsync(Guid.NewGuid(), default);
        Assert.Empty(result);
    }
}
