using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Dashboard;

public sealed class GetProcessingMetricsHandler(IDashboardRepository repository)
{
    public Task<ProcessingMetricsDto> HandleAsync(CancellationToken ct) => repository.GetProcessingMetricsAsync(ct);
}
