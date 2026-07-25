using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Application.Operations.GetOperationEvents;
using InvestOps.Application.Operations.ListProcessingEvents;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class ProcessingEventReadRepository(IDbSession session) : IProcessingEventReadRepository
{
    private const string BaseJoin = """
        FROM processing_events pe
        JOIN operations o ON o.id = pe.operation_id
        """;

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

    public async Task<PagedResult<ProcessingEventListItemDto>> ListAsync(ProcessingEventsQuery query, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var where = string.Empty;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            where = "WHERE pe.status = @Status";
            parameters.Add("Status", query.Status);
        }

        var orderBy = ProcessingEventsSortClauseBuilder.Build(query.Sort);
        var conn = await session.GetConnectionAsync(ct);

        var countSql = $"SELECT count(*) {BaseJoin} {where};";
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(countSql, parameters, session.Transaction, cancellationToken: ct));

        parameters.Add("Limit", query.NormalizedPageSize);
        parameters.Add("Offset", query.Offset);
        var itemsSql = $"""
            SELECT pe.id, pe.operation_id, o.external_id,
                   pe.event_type, pe.status, pe.attempt_count, pe.error_message,
                   pe.created_at, pe.processed_at
            {BaseJoin} {where} ORDER BY {orderBy} LIMIT @Limit OFFSET @Offset;
            """;
        var items = (await conn.QueryAsync<ProcessingEventListItemDto>(new CommandDefinition(itemsSql, parameters, session.Transaction, cancellationToken: ct))).AsList();

        return new PagedResult<ProcessingEventListItemDto>(items, (int)total, query.NormalizedPage, query.NormalizedPageSize);
    }
}
