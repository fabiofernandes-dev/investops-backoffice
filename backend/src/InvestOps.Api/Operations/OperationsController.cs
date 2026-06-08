using FluentValidation;
using InvestOps.Application.Operations.ImportOperation;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Operations;

[ApiController]
[Route("api/operations")]
public sealed class OperationsController(
    ImportOperationHandler importHandler,
    IValidator<ImportOperationRequest> importValidator) : ControllerBase
{
    [HttpPost("import")]
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
                ? Created($"/api/operations/{result.Id}", result)
                : Ok(result);
        }
        catch (ReferenceNotFoundException ex)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Referência inválida", Detail = ex.Message });
        }
    }

    private Guid GetCorrelationId() =>
        Request.Headers.TryGetValue("X-Correlation-Id", out var value) && Guid.TryParse(value, out var id)
            ? id
            : Guid.NewGuid();
}
