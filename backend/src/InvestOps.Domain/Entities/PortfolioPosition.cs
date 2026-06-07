namespace InvestOps.Domain.Entities;

public class PortfolioPosition
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal GrossAmount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
