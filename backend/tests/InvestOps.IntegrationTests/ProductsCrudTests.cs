using InvestOps.Application.Common;
using InvestOps.Application.Products;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ProductsCrudTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlConnectionFactory _factory = new(fixture.ConnectionString);
    private DbSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new DbSession(_factory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _session.DisposeAsync().AsTask();

    private FinancialProductRepository Repo() => new(_session);
    private ListProductsHandler ListHandler() => new(Repo());
    private CreateProductHandler CreateHandler() => new(Repo());
    private UpdateProductHandler UpdateHandler() => new(Repo());
    private DeleteProductHandler DeleteHandler() => new(Repo());

    private static string UniqueTicker() => "TK" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Create_then_list_returns_product_with_expected_fields()
    {
        var ticker = UniqueTicker();
        var created = await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "Petrobras ON", ticker, 3, 2), default);

        Assert.Equal("Stock", created.ProductType);
        Assert.True(created.IsActive);

        var listed = (await ListHandler().HandleAsync(default)).Single(p => p.Id == created.Id);
        Assert.Equal("Stock", listed.ProductType);
        Assert.Equal("Petrobras ON", listed.Name);
        Assert.Equal(ticker, listed.Ticker);
        Assert.Equal(3, listed.RiskLevel);
        Assert.Equal(2, listed.LiquidityDays);
        Assert.True(listed.IsActive);
    }

    [Fact]
    public async Task Create_with_duplicate_ticker_throws_conflict()
    {
        var ticker = UniqueTicker();
        await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "First", ticker, 3, 2), default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateHandler().HandleAsync(new CreateProductRequest("Stock", "Second", ticker, 3, 2), default));
    }

    [Fact]
    public async Task Create_two_products_with_null_ticker_both_succeed()
    {
        var a = await CreateHandler().HandleAsync(new CreateProductRequest("Fund", "Fund A", null, 2, 30), default);
        var b = await CreateHandler().HandleAsync(new CreateProductRequest("Fund", "Fund B", null, 2, 30), default);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Null(a.Ticker);
        Assert.Null(b.Ticker);
    }

    [Fact]
    public async Task Update_changes_fields_and_active_flag()
    {
        var created = await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "Before", UniqueTicker(), 3, 2), default);
        var newTicker = UniqueTicker();

        var updated = await UpdateHandler().HandleAsync(created.Id, new UpdateProductRequest("Fund", "After", newTicker, 5, 10, false), default);

        Assert.True(updated);
        var listed = (await ListHandler().HandleAsync(default)).Single(p => p.Id == created.Id);
        Assert.Equal("Fund", listed.ProductType);
        Assert.Equal("After", listed.Name);
        Assert.Equal(newTicker, listed.Ticker);
        Assert.Equal(5, listed.RiskLevel);
        Assert.Equal(10, listed.LiquidityDays);
        Assert.False(listed.IsActive);
    }

    [Fact]
    public async Task Update_nonexistent_returns_false()
    {
        var updated = await UpdateHandler().HandleAsync(Guid.NewGuid(),
            new UpdateProductRequest("Stock", "Ghost", null, 3, 2, true), default);

        Assert.False(updated);
    }

    [Fact]
    public async Task Update_with_another_products_ticker_throws_conflict()
    {
        var tickerA = UniqueTicker();
        await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "Owns A", tickerA, 3, 2), default);
        var b = await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "Owns B", UniqueTicker(), 3, 2), default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            UpdateHandler().HandleAsync(b.Id, new UpdateProductRequest("Stock", "Owns B", tickerA, 3, 2, true), default));
    }

    [Fact]
    public async Task Delete_soft_deactivates_product()
    {
        var created = await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "To Delete", UniqueTicker(), 3, 2), default);

        var deleted = await DeleteHandler().HandleAsync(created.Id, default);

        Assert.True(deleted);
        var listed = (await ListHandler().HandleAsync(default)).Single(p => p.Id == created.Id);
        Assert.False(listed.IsActive);
    }

    [Fact]
    public async Task Delete_nonexistent_returns_false()
    {
        var deleted = await DeleteHandler().HandleAsync(Guid.NewGuid(), default);
        Assert.False(deleted);
    }

    [Fact]
    public async Task Redelete_inactive_product_still_returns_true()
    {
        var created = await CreateHandler().HandleAsync(new CreateProductRequest("Stock", "Twice", UniqueTicker(), 3, 2), default);

        Assert.True(await DeleteHandler().HandleAsync(created.Id, default));
        Assert.True(await DeleteHandler().HandleAsync(created.Id, default));
    }
}
