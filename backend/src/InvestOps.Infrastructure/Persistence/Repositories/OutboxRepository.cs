using System.Data.Common;
using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository : IOutboxRepository
{
    public async Task InsertAsync(DbConnection conn, DbTransaction tx, OutboxMessage message, CancellationToken ct)
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
        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, tx, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(DbConnection conn, int batchSize, CancellationToken ct)
    {
        const string sql = """
            SELECT id, event_type, version, payload, status, attempts, created_at, published_at, last_error
            FROM outbox_messages
            WHERE status = 'Pending'
            ORDER BY created_at
            LIMIT @batchSize;
            """;
        var rows = await conn.QueryAsync<OutboxMessage>(new CommandDefinition(sql, new { batchSize }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task MarkPublishedAsync(DbConnection conn, Guid id, DateTimeOffset publishedAt, CancellationToken ct)
    {
        const string sql = "UPDATE outbox_messages SET status = 'Published', published_at = @publishedAt WHERE id = @id;";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id, publishedAt }, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(DbConnection conn, Guid id, string error, CancellationToken ct)
    {
        const string sql = "UPDATE outbox_messages SET attempts = attempts + 1, last_error = @error WHERE id = @id;";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id, error }, cancellationToken: ct));
    }
}
