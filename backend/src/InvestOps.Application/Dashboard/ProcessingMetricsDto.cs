namespace InvestOps.Application.Dashboard;

public sealed record ProcessingMetricsDto(
    IReadOnlyDictionary<string, long> OperationsByStatus,
    long OutboxPending);
