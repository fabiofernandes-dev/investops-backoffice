using InvestOps.Application.Dashboard;

namespace InvestOps.Application.Abstractions;

public interface IDashboardRepository
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<ProcessingMetricsDto> GetProcessingMetricsAsync(CancellationToken ct);
}
