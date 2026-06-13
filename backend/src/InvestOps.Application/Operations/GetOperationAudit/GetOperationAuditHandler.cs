using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Operations.GetOperationAudit;

public sealed class GetOperationAuditHandler(IAuditRepository repository)
{
    public Task<IReadOnlyList<AuditLogDto>> HandleAsync(Guid operationId, CancellationToken ct)
        => repository.GetByOperationAsync(operationId, ct);
}
