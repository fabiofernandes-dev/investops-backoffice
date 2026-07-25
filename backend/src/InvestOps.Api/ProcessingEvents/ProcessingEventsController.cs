using InvestOps.Application.Operations.ListProcessingEvents;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.ProcessingEvents;

[ApiController]
[Route("api/processing-events")]
public sealed class ProcessingEventsController(ListProcessingEventsHandler listHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ProcessingEventsQuery query, CancellationToken ct)
        => Ok(await listHandler.HandleAsync(query, ct));
}
