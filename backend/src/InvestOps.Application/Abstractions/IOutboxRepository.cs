using System.Data.Common;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IOutboxRepository
{
    Task InsertAsync(DbConnection conn, DbTransaction tx, OutboxMessage message, CancellationToken ct);
}
