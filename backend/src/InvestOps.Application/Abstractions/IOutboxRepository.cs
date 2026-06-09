using System.Data.Common;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IOutboxRepository
{
    Task InsertAsync(DbConnection conn, DbTransaction tx, OutboxMessage message, CancellationToken ct);
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(DbConnection conn, int batchSize, CancellationToken ct);
    Task MarkPublishedAsync(DbConnection conn, Guid id, DateTimeOffset publishedAt, CancellationToken ct);
    Task MarkFailedAsync(DbConnection conn, Guid id, string error, CancellationToken ct);
}
