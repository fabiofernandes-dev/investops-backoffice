using FluentValidation;
using InvestOps.Application.Common;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Application.Reconciliation.ImportReconciliation;
using InvestOps.Application.Reconciliation.IgnoreDivergence;
using InvestOps.Application.Reconciliation.ListDivergences;
using InvestOps.Application.Reconciliation.ResolveDivergence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InvestOps.Api.Reconciliation;

[ApiController]
[Route("api/reconciliations")]
public sealed class ReconciliationsController(
    ImportReconciliationHandler importHandler,
    IValidator<ImportReconciliationRequest> importValidator,
    ListDivergencesHandler listHandler,
    ResolveDivergenceHandler resolveHandler,
    IgnoreDivergenceHandler ignoreHandler) : ControllerBase
{
    [HttpPost("import")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Import([FromForm] string source, [FromForm] DateOnly referenceDate, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Arquivo CSV obrigatório" });
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Arquivo deve ser .csv" });

        IReadOnlyList<InvestOps.Domain.Reconciliation.CustodianRow> rows;
        try { await using var stream = file.OpenReadStream(); rows = CsvCustodianStatementParser.Parse(stream); }
        catch (CsvFormatException ex) { return BadRequest(new ProblemDetails { Title = "CSV inválido", Detail = ex.Message }); }

        var request = new ImportReconciliationRequest(source, referenceDate, rows);
        var validation = await importValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var e in validation.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
            return ValidationProblem(ModelState);
        }
        try { return Ok(await importHandler.HandleAsync(request, ct)); }
        catch (ReferenceNotFoundException ex) { return UnprocessableEntity(new ProblemDetails { Title = "Referência inválida", Detail = ex.Message }); }
    }

    [HttpGet("divergences")]
    public async Task<IActionResult> Divergences([FromQuery] DivergencesQuery query, CancellationToken ct)
        => Ok(await listHandler.HandleAsync(query, ct));

    [HttpPost("divergences/{id:guid}/resolve")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct) => await Act(() => resolveHandler.HandleAsync(id, ct));

    [HttpPost("divergences/{id:guid}/ignore")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Ignore(Guid id, CancellationToken ct) => await Act(() => ignoreHandler.HandleAsync(id, ct));

    private async Task<IActionResult> Act(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (ConflictException ex) { return Conflict(new ProblemDetails { Title = "Divergência não está pendente", Detail = ex.Message }); }
        catch (ReferenceNotFoundException ex) { return NotFound(new ProblemDetails { Title = "Divergência não encontrada", Detail = ex.Message }); }
    }
}
