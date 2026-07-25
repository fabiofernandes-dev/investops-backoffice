namespace InvestOps.Domain.Reconciliation;

public readonly record struct CustodianRow(Guid AccountId, Guid ProductId, decimal CustodianQuantity);
public readonly record struct InternalPosition(Guid AccountId, Guid ProductId, decimal Quantity);
public readonly record struct DivergenceResult(Guid AccountId, Guid ProductId, decimal InternalQuantity, decimal CustodianQuantity);

public static class ReconciliationComparer
{
    public static IReadOnlyList<DivergenceResult> Compare(
        IReadOnlyList<CustodianRow> custodianRows, IReadOnlyList<InternalPosition> internalPositions)
    {
        var internalByPair = internalPositions.ToDictionary(p => (p.AccountId, p.ProductId), p => p.Quantity);
        var divergences = new List<DivergenceResult>();
        foreach (var row in custodianRows)
        {
            var internalQty = internalByPair.TryGetValue((row.AccountId, row.ProductId), out var q) ? q : 0m;
            if (internalQty != row.CustodianQuantity)
                divergences.Add(new DivergenceResult(row.AccountId, row.ProductId, internalQty, row.CustodianQuantity));
        }
        return divergences;
    }
}
