using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Dashboard;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class DashboardRepository(IDbSession session) : IDashboardRepository
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
              (SELECT count(*) FROM operations WHERE created_at >= date_trunc('day', now()))                       AS imported_today,
              (SELECT count(*) FROM operations WHERE status='Processed')                                            AS processed,
              (SELECT count(*) FROM operations WHERE status='Rejected')                                             AS rejected,
              (SELECT count(*) FROM operations WHERE status='Failed')                                               AS failed,
              (SELECT count(*) FROM reconciliation_divergences WHERE status='Pending')                              AS divergences_pending,
              (SELECT avg(extract(epoch from (processed_at - created_at)))::double precision
                 FROM processing_events WHERE processed_at IS NOT NULL)                                            AS average_processing_seconds;
            """;
        var conn = await session.GetConnectionAsync(ct);
        return await conn.QuerySingleAsync<DashboardSummaryDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
    }

    public async Task<ProcessingMetricsDto> GetProcessingMetricsAsync(CancellationToken ct)
    {
        var conn = await session.GetConnectionAsync(ct);

        var rows = await conn.QueryAsync<(string Status, long Count)>(
            new CommandDefinition("SELECT status, count(*) AS count FROM operations GROUP BY status;", transaction: session.Transaction, cancellationToken: ct));
        var byStatus = rows.ToDictionary(r => r.Status, r => r.Count);

        var outboxPending = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT count(*) FROM outbox_messages WHERE status='Pending';", transaction: session.Transaction, cancellationToken: ct));

        return new ProcessingMetricsDto(byStatus, outboxPending);
    }
}
