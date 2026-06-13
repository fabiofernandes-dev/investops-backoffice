using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Operations.ListOperations;

public sealed class ListOperationsHandler(IOperationRepository operations)
{
    public async Task<PagedResult<OperationListItemDto>> HandleAsync(ListOperationsQuery query, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(query.Status) && !Enum.TryParse<OperationStatus>(query.Status, out _))
            return new PagedResult<OperationListItemDto>([], 0, query.NormalizedPage, query.NormalizedPageSize);

        var (items, total) = await operations.ListAsync(query, ct);
        return new PagedResult<OperationListItemDto>(items, total, query.NormalizedPage, query.NormalizedPageSize);
    }
}
