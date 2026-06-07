using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public class FinancialProduct
{
    public Guid Id { get; set; }
    public ProductType ProductType { get; set; }
    public string Name { get; set; } = default!;
    public string? Ticker { get; set; }
    public int RiskLevel { get; set; }
    public int LiquidityDays { get; set; }
    public bool IsActive { get; set; }
}
