using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string DocumentNumber { get; set; } = default!;
    public InvestorProfile InvestorProfile { get; set; }
    public CustomerStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
