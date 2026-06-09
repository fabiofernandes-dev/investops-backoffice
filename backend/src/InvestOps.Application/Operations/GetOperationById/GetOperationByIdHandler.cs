using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Operations.GetOperationById;

public sealed class GetOperationByIdHandler(IOperationRepository operations)
{
    public async Task<OperationDto?> HandleAsync(Guid id, CancellationToken ct)
    {
        var operation = await operations.GetByIdAsync(id, ct);
        return operation is null ? null : OperationDto.From(operation);
    }
}
