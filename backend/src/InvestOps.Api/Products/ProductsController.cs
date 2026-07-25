using FluentValidation;
using InvestOps.Application.Common;
using InvestOps.Application.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InvestOps.Api.Products;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    ListProductsHandler list,
    CreateProductHandler create, IValidator<CreateProductRequest> createValidator,
    UpdateProductHandler update, IValidator<UpdateProductRequest> updateValidator,
    DeleteProductHandler delete) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await list.HandleAsync(ct));

    [HttpPost]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }
        try
        {
            var dto = await create.HandleAsync(request, ct);
            return Created($"/api/products/{dto.Id}", dto);
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = "Ticker duplicado", Detail = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var validation = await updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }
        try
        {
            return await update.HandleAsync(id, request, ct) ? NoContent() : NotFound();
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = "Ticker duplicado", Detail = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await delete.HandleAsync(id, ct) ? NoContent() : NotFound();
}
