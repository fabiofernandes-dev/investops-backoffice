using InvestOps.Application.Operations.ListProcessingEvents;

namespace InvestOps.UnitTests;

public class ProcessingEventsSortClauseBuilderTests
{
    [Theory]
    [InlineData("createdAt:asc", "pe.created_at ASC")]
    [InlineData("createdAt:desc", "pe.created_at DESC")]
    [InlineData("status", "pe.status DESC")]
    [InlineData("attemptCount:asc", "pe.attempt_count ASC")]
    public void Build_maps_whitelisted_columns(string sort, string expected)
        => Assert.Equal(expected, ProcessingEventsSortClauseBuilder.Build(sort));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown:asc")]
    [InlineData("status; DROP TABLE processing_events;--")]
    public void Build_falls_back_to_default(string? sort)
        => Assert.Equal(ProcessingEventsSortClauseBuilder.Default, ProcessingEventsSortClauseBuilder.Build(sort));
}
