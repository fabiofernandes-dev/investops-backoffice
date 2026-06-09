using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Operations.GetOperationById;

public sealed class GetOperationByIdHandler(IDbConnectionFactory connectionFactory, IOperationRepository operations)
{
    public async Task<OperationDto?> HandleAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        var operation = await operations.GetByIdAsync(conn, id, ct);
        return operation is null ? null : OperationDto.From(operation);
    }
}
