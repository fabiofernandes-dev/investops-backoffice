namespace InvestOps.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public string Action { get; set; } = default!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
