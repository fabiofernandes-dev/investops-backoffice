using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public class InvestmentAccount
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string AccountNumber { get; set; } = default!;
    public AccountStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
