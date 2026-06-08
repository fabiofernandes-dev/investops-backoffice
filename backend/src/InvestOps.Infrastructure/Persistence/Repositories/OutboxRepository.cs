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
}
