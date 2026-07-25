using InvestOps.Application.Reconciliation.ListDivergences;

namespace InvestOps.UnitTests;

public class DivergencesSortClauseBuilderTests
{
    [Theory]
    [InlineData("createdAt:asc", "d.created_at ASC")]
    [InlineData("createdAt:desc", "d.created_at DESC")]
    [InlineData("status", "d.status DESC")]
    [InlineData("difference:asc", "(d.custodian_quantity - d.internal_quantity) ASC")]
    public void Build_maps_whitelisted_columns(string sort, string expected)
        => Assert.Equal(expected, DivergencesSortClauseBuilder.Build(sort));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown:asc")]
    [InlineData("status; DROP TABLE reconciliation_divergences;--")]
    public void Build_falls_back_to_default(string? sort)
        => Assert.Equal(DivergencesSortClauseBuilder.Default, DivergencesSortClauseBuilder.Build(sort));
}
