using InvestOps.Application.Abstractions;

namespace InvestOps.Application.Dashboard;

public sealed class GetDashboardSummaryHandler(IDashboardRepository repository)
{
    public Task<DashboardSummaryDto> HandleAsync(CancellationToken ct) => repository.GetSummaryAsync(ct);
}
