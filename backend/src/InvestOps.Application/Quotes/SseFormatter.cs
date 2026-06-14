using System.Text.Json;

namespace InvestOps.Application.Quotes;

// Serializa frames SSE (Server-Sent Events). Helper puro (sem HttpContext) → testável em unit.
public static class SseFormatter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Format(QuoteDto quote) => $"data: {JsonSerializer.Serialize(quote, Options)}\n\n";

    public static string FormatError(string message) =>
        $"event: error\ndata: {JsonSerializer.Serialize(new { message }, Options)}\n\n";
}
