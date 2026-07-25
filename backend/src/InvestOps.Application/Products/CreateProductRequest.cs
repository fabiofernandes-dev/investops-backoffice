namespace InvestOps.Application.Products;

public sealed record CreateProductRequest(string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays);
