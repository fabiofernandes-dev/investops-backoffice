using InvestOps.Application.Operations.GetOperationEvents;

namespace InvestOps.Application.Abstractions;

public interface IProcessingEventReadRepository
{
    Task<IReadOnlyList<ProcessingEventDto>> GetByOperationAsync(Guid operationId, CancellationToken ct);
}
