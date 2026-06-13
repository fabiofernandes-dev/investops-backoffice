namespace InvestOps.Application.Dashboard;

public sealed record DashboardSummaryDto(
    long ImportedToday,
    long Processed,
    long Rejected,
    long Failed,
    long DivergencesPending,
    double? AverageProcessingSeconds);
