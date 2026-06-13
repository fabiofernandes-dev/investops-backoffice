using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Operations.GetOperationEvents;

public sealed class GetOperationEventsHandler(IProcessingEventReadRepository repository)
{
    public Task<IReadOnlyList<ProcessingEventDto>> HandleAsync(Guid operationId, CancellationToken ct)
        => repository.GetByOperationAsync(operationId, ct);
}
