using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.GetOperationEvents;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class ProcessingEventReadRepository(IDbSession session) : IProcessingEventReadRepository
{
    public async Task<IReadOnlyList<ProcessingEventDto>> GetByOperationAsync(Guid operationId, CancellationToken ct)
    {
        const string sql = """
            SELECT event_type, status, attempt_count, error_message, created_at, processed_at
            FROM processing_events
            WHERE operation_id = @operationId
            ORDER BY created_at;
            """;
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProcessingEventDto>(
            new CommandDefinition(sql, new { operationId }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
