using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository(IDbSession session) : IOutboxRepository
{
    public async Task InsertAsync(OutboxMessage message, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO outbox_messages (id, event_type, version, payload, status, attempts, created_at)
            VALUES (@Id, @EventType, @Version, @Payload::jsonb, @Status, @Attempts, @CreatedAt);
            """;
        var parameters = new
        {
            message.Id,
            message.EventType,
            message.Version,
            message.Payload,
            Status = message.Status.ToString(),
            message.Attempts,
            message.CreatedAt
        };
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, session.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        const string sql = """
            SELECT id, event_type, version, payload, status, attempts, created_at, published_at, last_error
            FROM outbox_messages
            WHERE status = 'Pending' AND source = 'api'
            ORDER BY created_at
            LIMIT @batchSize;
            """;
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<OutboxMessage>(
            new CommandDefinition(sql, new { batchSize }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task MarkPublishedAsync(Guid id, DateTimeOffset publishedAt, CancellationToken ct)
    {
        const string sql = "UPDATE outbox_messages SET status = 'Published', published_at = @publishedAt WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id, publishedAt }, session.Transaction, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct)
    {
        const string sql = "UPDATE outbox_messages SET attempts = attempts + 1, last_error = @error WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id, error }, session.Transaction, cancellationToken: ct));
    }
}
