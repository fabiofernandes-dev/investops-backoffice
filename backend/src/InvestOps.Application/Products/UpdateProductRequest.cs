namespace InvestOps.Application.Products;

public sealed record UpdateProductRequest(string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays, bool IsActive);
