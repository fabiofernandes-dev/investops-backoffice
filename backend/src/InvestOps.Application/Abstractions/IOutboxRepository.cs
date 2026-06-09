using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IOutboxRepository
{
    Task InsertAsync(OutboxMessage message, CancellationToken ct);
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct);
    Task MarkPublishedAsync(Guid id, DateTimeOffset publishedAt, CancellationToken ct);
    Task MarkFailedAsync(Guid id, string error, CancellationToken ct);
}
