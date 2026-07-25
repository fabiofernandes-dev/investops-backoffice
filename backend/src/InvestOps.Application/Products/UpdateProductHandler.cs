using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Products;

public sealed class UpdateProductHandler(IFinancialProductRepository repository)
{
    public async Task<bool> HandleAsync(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = new FinancialProduct
        {
            Id = id,
            ProductType = Enum.Parse<ProductType>(request.ProductType),
            Name = request.Name,
            Ticker = string.IsNullOrWhiteSpace(request.Ticker) ? null : request.Ticker,
            RiskLevel = request.RiskLevel,
            LiquidityDays = request.LiquidityDays,
            IsActive = request.IsActive
        };
        return await repository.UpdateAsync(product, ct) > 0;
    }
}
