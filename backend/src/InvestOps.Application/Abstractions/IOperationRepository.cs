using System.Data.Common;
using InvestOps.Application.Operations.ListOperations;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IOperationRepository
{
    Task<Operation?> GetByExternalIdAsync(DbConnection conn, string externalId, CancellationToken ct);
    Task InsertAsync(DbConnection conn, DbTransaction tx, Operation op, CancellationToken ct);
    Task<Operation?> GetByIdAsync(DbConnection conn, Guid id, CancellationToken ct);
    Task<(IReadOnlyList<Operation> Items, int Total)> ListAsync(DbConnection conn, ListOperationsQuery query, CancellationToken ct);
}
