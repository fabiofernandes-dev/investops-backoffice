namespace InvestOps.Application.Products;

public sealed record ProductDto(Guid Id, string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays, bool IsActive);
