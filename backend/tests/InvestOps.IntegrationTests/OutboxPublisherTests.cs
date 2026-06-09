using System.Text;
using System.Text.Json;
using Dapper;
using InvestOps.Infrastructure.Messaging;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Repositories;
using InvestOps.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class OutboxPublisherTests(PostgresFixture pg, RabbitMqFixture mq) : IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task Publishes_pending_message_and_marks_it_published()
    {
        var options = new RabbitMqOptions
        {
            Host = mq.Host,
            Port = mq.Port,
            Username = mq.Username,
            Password = mq.Password,
            Exchange = "investops",
            BatchSize = 50
        };

        // fila + binding ANTES de publicar (topic exchange descarta mensagens sem binding)
        var connectionFactory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password
        };
        using var consumerConnection = connectionFactory.CreateConnection();
        using var consumerChannel = consumerConnection.CreateModel();
        consumerChannel.ExchangeDeclare(options.Exchange, ExchangeType.Topic, durable: true);
        var queue = consumerChannel.QueueDeclare().QueueName;
        consumerChannel.QueueBind(queue, options.Exchange, "OperationImported");

        // outbox pendente
        var outboxId = Guid.NewGuid();
        const string payload = "{\"hello\":\"world\"}";
        var dbFactory = new NpgsqlConnectionFactory(pg.ConnectionString);
        await using (var conn = await dbFactory.OpenConnectionAsync())
        {
            // isola o teste: a Postgres é compartilhada na collection e outros testes deixam Pending
            await conn.ExecuteAsync("DELETE FROM outbox_messages;");
            await conn.ExecuteAsync(
                "INSERT INTO outbox_messages (id, event_type, version, payload, status, attempts, created_at) VALUES (@outboxId, 'OperationImported', 1, @payload::jsonb, 'Pending', 0, now());",
                new { outboxId, payload });
        }

        var publisher = new OutboxPublisher(
            dbFactory, new OutboxRepository(), new RabbitMqEventBus(options), options,
            TimeProvider.System, NullLogger<OutboxPublisher>.Instance);

        var published = await publisher.PublishPendingAsync(default);
        Assert.True(published >= 1);

        BasicGetResult? received = null;
        for (var i = 0; i < 25 && received is null; i++)
        {
            received = consumerChannel.BasicGet(queue, autoAck: true);
            if (received is null) await Task.Delay(100);
        }

        Assert.NotNull(received);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(received!.Body.ToArray()));
        Assert.Equal("world", doc.RootElement.GetProperty("hello").GetString());

        await using var verifyConn = await dbFactory.OpenConnectionAsync();
        var status = await verifyConn.ExecuteScalarAsync<string>(
            "SELECT status FROM outbox_messages WHERE id = @outboxId;", new { outboxId });
        Assert.Equal("Published", status);
    }
}
