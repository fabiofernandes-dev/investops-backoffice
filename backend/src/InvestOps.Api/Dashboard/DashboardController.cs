using InvestOps.Application.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Dashboard;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(
    GetDashboardSummaryHandler summaryHandler,
    GetProcessingMetricsHandler metricsHandler) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await summaryHandler.HandleAsync(ct));

    [HttpGet("processing-metrics")]
    public async Task<IActionResult> ProcessingMetrics(CancellationToken ct) => Ok(await metricsHandler.HandleAsync(ct));
}
