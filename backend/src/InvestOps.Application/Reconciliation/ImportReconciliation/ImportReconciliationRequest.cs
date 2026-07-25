using InvestOps.Domain.Reconciliation;

namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public sealed record ImportReconciliationRequest(string Source, DateOnly ReferenceDate, IReadOnlyList<CustodianRow> Rows);
