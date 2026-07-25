namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public sealed record ImportReconciliationResult(
    Guid ImportId, string Source, DateOnly ReferenceDate, int RowsProcessed, int MatchedCount, int DivergenceCount);
