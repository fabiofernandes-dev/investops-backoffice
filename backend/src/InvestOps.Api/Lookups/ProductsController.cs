using InvestOps.Application.Lookups;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Lookups;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ListProductsHandler handler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await handler.HandleAsync(ct));
}
