using InvestOps.Application.Positions;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Positions;

[ApiController]
[Route("api/positions")]
public sealed class PositionsController(
    ListPositionsHandler listHandler,
    GetPositionsByAccountHandler byAccountHandler,
    GetPositionsByCustomerHandler byCustomerHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PositionsQuery query, CancellationToken ct)
        => Ok(await listHandler.HandleAsync(query, ct));

    [HttpGet("by-account/{accountId:guid}")]
    public async Task<IActionResult> ByAccount(Guid accountId, CancellationToken ct)
        => Ok(await byAccountHandler.HandleAsync(accountId, ct));

    [HttpGet("by-customer/{customerId:guid}")]
    public async Task<IActionResult> ByCustomer(Guid customerId, CancellationToken ct)
        => Ok(await byCustomerHandler.HandleAsync(customerId, ct));
}
