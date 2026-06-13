using InvestOps.Application.Operations.GetOperationAudit;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IAuditRepository
{
    Task InsertAsync(AuditLog log, CancellationToken ct);
    Task<IReadOnlyList<AuditLogDto>> GetByOperationAsync(Guid operationId, CancellationToken ct);
}
