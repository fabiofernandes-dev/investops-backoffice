using InvestOps.Application.Quotes;

namespace InvestOps.UnitTests;

public class SseFormatterTests
{
    [Fact]
    public void Format_emits_data_frame_with_camelCase_json()
    {
        var quote = new QuoteDto("prod-1", 38.42m, new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero));

        var frame = SseFormatter.Format(quote);

        Assert.StartsWith("data: ", frame);
        Assert.EndsWith("\n\n", frame);
        Assert.Contains("\"productId\":\"prod-1\"", frame);
        Assert.Contains("38.42", frame);
    }

    [Fact]
    public void FormatError_emits_error_event_with_message()
    {
        var frame = SseFormatter.FormatError("Falha no stream.");

        Assert.StartsWith("event: error\n", frame);
        Assert.Contains("\"message\":\"Falha no stream.\"", frame);
        Assert.EndsWith("\n\n", frame);
    }
}
