using System.Text;
using InvestOps.Application.Abstractions;
using RabbitMQ.Client;

namespace InvestOps.Infrastructure.Messaging;

public sealed class RabbitMqEventBus(RabbitMqOptions options) : IEventBus, IDisposable
{
    private readonly object _gate = new();
    private IConnection? _connection;

    public Task PublishAsync(string eventType, string payloadJson, CancellationToken ct)
    {
        using var channel = GetConnection().CreateModel();
        channel.ExchangeDeclare(options.Exchange, ExchangeType.Topic, durable: true);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";

        channel.BasicPublish(
            exchange: options.Exchange,
            routingKey: eventType,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(payloadJson));

        return Task.CompletedTask;
    }

    private IConnection GetConnection()
    {
        if (_connection is { IsOpen: true })
            return _connection;

        lock (_gate)
        {
            if (_connection is { IsOpen: true })
                return _connection;

            var factory = new ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.Username,
                Password = options.Password,
                AutomaticRecoveryEnabled = true
            };
            _connection = factory.CreateConnection();
            return _connection;
        }
    }

    public void Dispose() => _connection?.Dispose();
}
