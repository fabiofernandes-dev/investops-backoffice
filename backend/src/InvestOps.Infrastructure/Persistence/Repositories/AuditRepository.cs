using System.Data.Common;
using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Entities;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class AuditRepository : IAuditRepository
{
    public async Task InsertAsync(DbConnection conn, DbTransaction tx, AuditLog log, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO audit_logs (id, entity_name, entity_id, action, old_value, new_value, user_id, created_at)
            VALUES (@Id, @EntityName, @EntityId, @Action, @OldValue::jsonb, @NewValue::jsonb, @UserId, @CreatedAt);
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, log, tx, cancellationToken: ct));
    }
}
