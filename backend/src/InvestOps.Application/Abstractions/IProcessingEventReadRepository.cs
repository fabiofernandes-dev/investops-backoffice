using InvestOps.Application.Common;
using InvestOps.Application.Operations.GetOperationEvents;
using InvestOps.Application.Operations.ListProcessingEvents;

namespace InvestOps.Application.Abstractions;

public interface IProcessingEventReadRepository
{
    Task<IReadOnlyList<ProcessingEventDto>> GetByOperationAsync(Guid operationId, CancellationToken ct);
    Task<PagedResult<ProcessingEventListItemDto>> ListAsync(ProcessingEventsQuery query, CancellationToken ct);
}
