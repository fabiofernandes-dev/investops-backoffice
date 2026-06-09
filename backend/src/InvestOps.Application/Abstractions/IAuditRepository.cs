using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IAuditRepository
{
    Task InsertAsync(AuditLog log, CancellationToken ct);
}
