using InvestOps.Application.Lookups;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Lookups;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(ListCustomersHandler handler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await handler.HandleAsync(ct));
}
