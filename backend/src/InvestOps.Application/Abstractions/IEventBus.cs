namespace InvestOps.Application.Abstractions;

public interface IEventBus
{
    Task PublishAsync(string eventType, string payloadJson, CancellationToken ct);
}
