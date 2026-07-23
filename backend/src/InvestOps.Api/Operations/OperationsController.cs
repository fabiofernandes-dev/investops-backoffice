using FluentValidation;
using InvestOps.Application.Operations.GetOperationAudit;
using InvestOps.Application.Operations.GetOperationById;
using InvestOps.Application.Operations.GetOperationEvents;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Application.Operations.ListOperations;
using InvestOps.Application.Operations.ReprocessOperation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InvestOps.Api.Operations;

[ApiController]
[Route("api/operations")]
public sealed class OperationsController(
    ImportOperationHandler importHandler,
    IValidator<ImportOperationRequest> importValidator,
    GetOperationByIdHandler getByIdHandler,
    ListOperationsHandler listHandler,
    GetOperationEventsHandler eventsHandler,
    GetOperationAuditHandler auditHandler,
    ReprocessOperationHandler reprocessHandler) : ControllerBase
{
    [HttpPost("import")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Import(ImportOperationRequest request, CancellationToken ct)
    {
        var validation = await importValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var correlationId = GetCorrelationId();
        try
        {
            var result = await importHandler.HandleAsync(request, correlationId, ct);
            return result.Created
                ? CreatedAtAction(nameof(GetById), new { id = result.Id }, result)
                : Ok(result);
        }
        catch (ReferenceNotFoundException ex)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Referência inválida", Detail = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var operation = await getByIdHandler.HandleAsync(id, ct);
        return operation is null ? NotFound() : Ok(operation);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListOperationsQuery query, CancellationToken ct)
    {
        var result = await listHandler.HandleAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct)
        => Ok(await eventsHandler.HandleAsync(id, ct));

    [HttpGet("{id:guid}/audit")]
    public async Task<IActionResult> Audit(Guid id, CancellationToken ct)
        => Ok(await auditHandler.HandleAsync(id, ct));

    [HttpPost("{id:guid}/reprocess")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
    {
        try
        {
            await reprocessHandler.HandleAsync(id, GetCorrelationId(), ct);
            return Accepted();
        }
        catch (ReferenceNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Operação não encontrada", Detail = ex.Message });
        }
    }

    private Guid GetCorrelationId() =>
        Request.Headers.TryGetValue("X-Correlation-Id", out var value) && Guid.TryParse(value, out var id)
            ? id
            : Guid.NewGuid();
}
