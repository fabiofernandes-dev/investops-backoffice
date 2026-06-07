namespace InvestOps.Domain.Entities;

public class ProcessingEvent
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public string EventType { get; set; } = default!;
    public string? Payload { get; set; }
    public string Status { get; set; } = default!;
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
