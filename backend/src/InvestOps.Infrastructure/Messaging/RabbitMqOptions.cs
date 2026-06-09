namespace InvestOps.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "investops";
    public string Password { get; set; } = "investops";
    public string Exchange { get; set; } = "investops";
    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 50;
}
