using InvestOps.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestOps.Infrastructure.Messaging;

public sealed class OutboxPublisher(
    IDbConnectionFactory connectionFactory,
    IOutboxRepository outbox,
    IEventBus bus,
    RabbitMqOptions options,
    TimeProvider clock,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo do OutboxPublisher");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
        }
    }

    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        var pending = await outbox.GetPendingAsync(conn, options.BatchSize, ct);

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await bus.PublishAsync(message.EventType, message.Payload, ct);
                await outbox.MarkPublishedAsync(conn, message.Id, clock.GetUtcNow(), ct);
                published++;
            }
            catch (Exception ex)
            {
                await outbox.MarkFailedAsync(conn, message.Id, ex.Message, ct);
                logger.LogWarning(ex, "Falha ao publicar outbox {OutboxId}", message.Id);
            }
        }

        return published;
    }
}
