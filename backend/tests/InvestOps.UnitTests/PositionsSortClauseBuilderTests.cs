using InvestOps.Application.Positions;

namespace InvestOps.UnitTests;

public class PositionsSortClauseBuilderTests
{
    [Theory]
    [InlineData("updatedAt:desc", "p.updated_at DESC")]
    [InlineData("updatedAt:asc", "p.updated_at ASC")]
    [InlineData("grossAmount", "p.gross_amount DESC")]
    [InlineData("quantity:asc", "p.quantity ASC")]
    public void Build_maps_whitelisted_columns(string sort, string expected)
        => Assert.Equal(expected, PositionsSortClauseBuilder.Build(sort));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown:asc")]
    [InlineData("updated_at; DROP TABLE portfolio_positions;--")]
    public void Build_falls_back_to_default(string? sort)
        => Assert.Equal(PositionsSortClauseBuilder.Default, PositionsSortClauseBuilder.Build(sort));
}
