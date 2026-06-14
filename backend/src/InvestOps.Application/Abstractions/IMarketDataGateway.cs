using InvestOps.Application.Quotes;

namespace InvestOps.Application.Abstractions;

public interface IMarketDataGateway
{
    Task<QuoteDto> GetQuoteAsync(string productId, CancellationToken ct);
    IAsyncEnumerable<QuoteDto> StreamQuotesAsync(IEnumerable<string> productIds, CancellationToken ct);
}
