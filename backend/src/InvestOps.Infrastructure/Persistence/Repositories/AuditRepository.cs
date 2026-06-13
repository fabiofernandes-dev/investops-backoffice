using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.GetOperationAudit;
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

    public async Task<IReadOnlyList<AuditLogDto>> GetByOperationAsync(Guid operationId, CancellationToken ct)
    {
        const string sql = """
            SELECT action, old_value, new_value, user_id, created_at
            FROM audit_logs
            WHERE entity_name = 'Operation' AND entity_id = @entityId
            ORDER BY created_at;
            """;
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<AuditLogDto>(
            new CommandDefinition(sql, new { entityId = operationId.ToString() }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
