using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Operations.ListOperations;

public sealed class ListOperationsHandler(IDbConnectionFactory connectionFactory, IOperationRepository operations)
{
    public async Task<PagedResult<OperationDto>> HandleAsync(ListOperationsQuery query, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(query.Status) && !Enum.TryParse<OperationStatus>(query.Status, out _))
            return new PagedResult<OperationDto>([], 0, query.NormalizedPage, query.NormalizedPageSize);

        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        var (items, total) = await operations.ListAsync(conn, query, ct);

        var dtos = items.Select(OperationDto.From).ToList();
        return new PagedResult<OperationDto>(dtos, total, query.NormalizedPage, query.NormalizedPageSize);
    }
}
