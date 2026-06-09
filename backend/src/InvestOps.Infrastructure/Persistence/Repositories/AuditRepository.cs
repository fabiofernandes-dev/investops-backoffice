using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class AuditRepository(IDbSession session) : IAuditRepository
{
    public async Task InsertAsync(AuditLog log, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO audit_logs (id, entity_name, entity_id, action, old_value, new_value, user_id, created_at)
            VALUES (@Id, @EntityName, @EntityId, @Action, @OldValue::jsonb, @NewValue::jsonb, @UserId, @CreatedAt);
            """;
        var conn = await session.GetConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, log, session.Transaction, cancellationToken: ct));
    }
}
