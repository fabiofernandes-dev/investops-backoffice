using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = default!;
    public int Version { get; set; }
    public string Payload { get; set; } = default!;
    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? LastError { get; set; }
}
