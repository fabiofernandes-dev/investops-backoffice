using System.Data.Common;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IAuditRepository
{
    Task InsertAsync(DbConnection conn, DbTransaction tx, AuditLog log, CancellationToken ct);
}
