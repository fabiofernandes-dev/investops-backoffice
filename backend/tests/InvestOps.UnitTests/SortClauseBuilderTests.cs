using InvestOps.Application.Operations.ListOperations;

namespace InvestOps.UnitTests;

public class SortClauseBuilderTests
{
    [Theory]
    [InlineData("operationDate:desc", "operation_date DESC")]
    [InlineData("operationDate:asc", "operation_date ASC")]
    [InlineData("grossValue", "gross_value DESC")]
    [InlineData("status:asc", "status ASC")]
    public void Build_maps_whitelisted_columns(string sort, string expected)
        => Assert.Equal(expected, SortClauseBuilder.Build(sort));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknownColumn:asc")]
    [InlineData("operation_date; DROP TABLE operations;--")]
    public void Build_falls_back_to_default_for_invalid_input(string? sort)
        => Assert.Equal(SortClauseBuilder.Default, SortClauseBuilder.Build(sort));
}
