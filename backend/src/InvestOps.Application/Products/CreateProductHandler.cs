using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Products;

public sealed class CreateProductHandler(IFinancialProductRepository repository)
{
    public async Task<ProductDto> HandleAsync(CreateProductRequest request, CancellationToken ct)
    {
        var product = new FinancialProduct
        {
            Id = Guid.NewGuid(),
            ProductType = Enum.Parse<ProductType>(request.ProductType),
            Name = request.Name,
            Ticker = string.IsNullOrWhiteSpace(request.Ticker) ? null : request.Ticker,
            RiskLevel = request.RiskLevel,
            LiquidityDays = request.LiquidityDays,
            IsActive = true
        };
        await repository.InsertAsync(product, ct);
        return new ProductDto(product.Id, product.ProductType.ToString(), product.Name, product.Ticker, product.RiskLevel, product.LiquidityDays, product.IsActive);
    }
}
