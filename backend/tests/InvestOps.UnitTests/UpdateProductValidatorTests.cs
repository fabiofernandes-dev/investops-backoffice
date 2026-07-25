using InvestOps.Application.Products;

namespace InvestOps.UnitTests;

public class UpdateProductValidatorTests
{
    private readonly UpdateProductValidator _validator = new();

    private static UpdateProductRequest Valid() => new("Stock", "PETR4 ON", "PETR4", 3, 2, true);

    [Fact]
    public void Accepts_valid_request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Accepts_null_ticker()
    {
        Assert.True(_validator.Validate(Valid() with { Ticker = null }).IsValid);
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("stock")]
    [InlineData("2")]
    [InlineData("")]
    public void Rejects_invalid_product_type(string productType)
    {
        var result = _validator.Validate(Valid() with { ProductType = productType });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductRequest.ProductType));
    }

    [Fact]
    public void Rejects_empty_name()
    {
        var result = _validator.Validate(Valid() with { Name = "" });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductRequest.Name));
    }

    [Fact]
    public void Rejects_name_longer_than_200()
    {
        var result = _validator.Validate(Valid() with { Name = new string('a', 201) });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductRequest.Name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rejects_risk_level_outside_1_to_5(int riskLevel)
    {
        var result = _validator.Validate(Valid() with { RiskLevel = riskLevel });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductRequest.RiskLevel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Accepts_risk_level_boundaries(int riskLevel)
    {
        Assert.True(_validator.Validate(Valid() with { RiskLevel = riskLevel }).IsValid);
    }

    [Fact]
    public void Rejects_negative_liquidity_days()
    {
        var result = _validator.Validate(Valid() with { LiquidityDays = -1 });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductRequest.LiquidityDays));
    }

    [Fact]
    public void Rejects_ticker_longer_than_20()
    {
        var result = _validator.Validate(Valid() with { Ticker = new string('T', 21) });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductRequest.Ticker));
    }

    [Fact]
    public void Accepts_inactive_flag()
    {
        Assert.True(_validator.Validate(Valid() with { IsActive = false }).IsValid);
    }
}
