using InvestOps.Domain.Reconciliation;

namespace InvestOps.UnitTests;

public class ReconciliationComparerTests
{
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly Guid ProductB = Guid.NewGuid();

    [Fact]
    public void Equal_quantities_produce_no_divergence()
    {
        var custodian = new[] { new CustodianRow(AccountA, ProductA, 100m) };
        var internalPositions = new[] { new InternalPosition(AccountA, ProductA, 100m) };

        var result = ReconciliationComparer.Compare(custodian, internalPositions);

        Assert.Empty(result);
    }

    [Fact]
    public void Different_quantities_produce_divergence_with_both_quantities()
    {
        var custodian = new[] { new CustodianRow(AccountA, ProductA, 150m) };
        var internalPositions = new[] { new InternalPosition(AccountA, ProductA, 100m) };

        var result = ReconciliationComparer.Compare(custodian, internalPositions);

        var divergence = Assert.Single(result);
        Assert.Equal(AccountA, divergence.AccountId);
        Assert.Equal(ProductA, divergence.ProductId);
        Assert.Equal(100m, divergence.InternalQuantity);
        Assert.Equal(150m, divergence.CustodianQuantity);
    }

    [Fact]
    public void Pair_without_internal_position_yields_zero_internal_quantity_and_divergence()
    {
        var custodian = new[] { new CustodianRow(AccountA, ProductA, 42m) };
        var internalPositions = Array.Empty<InternalPosition>();

        var result = ReconciliationComparer.Compare(custodian, internalPositions);

        var divergence = Assert.Single(result);
        Assert.Equal(0m, divergence.InternalQuantity);
        Assert.Equal(42m, divergence.CustodianQuantity);
    }

    [Fact]
    public void Multiple_pairs_return_only_the_divergent_ones()
    {
        var custodian = new[]
        {
            new CustodianRow(AccountA, ProductA, 100m), // matches
            new CustodianRow(AccountA, ProductB, 200m), // diverges (internal 180)
            new CustodianRow(AccountB, ProductA, 50m)   // diverges (no internal)
        };
        var internalPositions = new[]
        {
            new InternalPosition(AccountA, ProductA, 100m),
            new InternalPosition(AccountA, ProductB, 180m)
        };

        var result = ReconciliationComparer.Compare(custodian, internalPositions);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.AccountId == AccountA && d.ProductId == ProductB && d.InternalQuantity == 180m && d.CustodianQuantity == 200m);
        Assert.Contains(result, d => d.AccountId == AccountB && d.ProductId == ProductA && d.InternalQuantity == 0m && d.CustodianQuantity == 50m);
    }
}
