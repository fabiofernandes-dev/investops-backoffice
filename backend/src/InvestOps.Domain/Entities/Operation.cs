using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public class Operation
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = default!;
    public Guid AccountId { get; set; }
    public Guid ProductId { get; set; }
    public OperationType OperationType { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal GrossValue { get; set; }
    public decimal NetValue { get; set; }
    public DateOnly OperationDate { get; set; }
    public DateOnly SettlementDate { get; set; }
    public OperationStatus Status { get; set; }
    public RejectionReason? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
