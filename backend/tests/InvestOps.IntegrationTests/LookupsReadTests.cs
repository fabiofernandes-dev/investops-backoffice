using Dapper;
using InvestOps.Application.Lookups;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class LookupsReadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private ListProductsHandler ProductsHandler() => new(new LookupRepository(_session));
    private ListCustomersHandler CustomersHandler() => new(new LookupRepository(_session));
    private ListAccountsHandler AccountsHandler() => new(new LookupRepository(_session));

    [Fact]
    public async Task Products_returns_seeded_product()
    {
        var productId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
            await conn.ExecuteAsync("INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active) VALUES (@productId,'Stock','Lookup Prod','LKP',3,2,true);", new { productId });

        var products = await ProductsHandler().HandleAsync(default);

        Assert.Contains(products, p => p.Id == productId && p.Name == "Lookup Prod" && p.Ticker == "LKP");
    }

    [Fact]
    public async Task Customers_returns_seeded_customer()
    {
        var customerId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
            await conn.ExecuteAsync("INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId,'Lookup Cust','999','Moderate','Active');", new { customerId });

        var customers = await CustomersHandler().HandleAsync(default);

        Assert.Contains(customers, c => c.Id == customerId && c.Name == "Lookup Cust");
    }

    [Fact]
    public async Task Accounts_returns_seeded_account_with_customer_name()
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using (var conn = await _factory.OpenConnectionAsync())
        {
            await conn.ExecuteAsync("INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES (@customerId,'Acc Cust','111','Moderate','Active');", new { customerId });
            await conn.ExecuteAsync("INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES (@accountId,@customerId,'ACC-LKP','Active');", new { accountId, customerId });
        }

        var accounts = await AccountsHandler().HandleAsync(default);

        Assert.Contains(accounts, a => a.Id == accountId && a.AccountNumber == "ACC-LKP" && a.CustomerName == "Acc Cust");
    }
}
