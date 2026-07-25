using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;

namespace InvestOps.Application.Operations.ListProcessingEvents;

public sealed class ListProcessingEventsHandler(IProcessingEventReadRepository repo)
{
    public Task<PagedResult<ProcessingEventListItemDto>> HandleAsync(ProcessingEventsQuery query, CancellationToken ct)
        => repo.ListAsync(query, ct);
}
