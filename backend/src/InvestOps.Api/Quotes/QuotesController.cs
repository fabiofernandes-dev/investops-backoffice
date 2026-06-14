using InvestOps.Application.Abstractions;
using InvestOps.Application.Quotes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Quotes;

[ApiController]
[Route("api/quotes")]
public sealed class QuotesController(IMarketDataGateway gateway) : ControllerBase
{
    [HttpGet("{productId}")]
    public async Task<IActionResult> Get(string productId, CancellationToken ct)
    {
        try
        {
            return Ok(await gateway.GetQuoteAsync(productId, ct));
        }
        catch (MarketDataUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = "Market data indisponível", Detail = ex.Message });
        }
    }

    [HttpGet("stream")]
    public async Task Stream([FromQuery] string? productIds, CancellationToken ct)
    {
        var ids = (productIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await foreach (var quote in gateway.StreamQuotesAsync(ids, ct))
            {
                await Response.WriteAsync(SseFormatter.Format(quote), ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // cliente desconectou — encerramento normal
        }
        catch (Exception)
        {
            await Response.WriteAsync(SseFormatter.FormatError("Falha no stream de cotações."), CancellationToken.None);
            await Response.Body.FlushAsync(CancellationToken.None);
        }
    }
}
