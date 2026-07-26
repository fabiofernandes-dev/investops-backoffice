using System.Globalization;
using System.Runtime.CompilerServices;
using Grpc.Core;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Quotes;
using InvestOps.MarketData.V1;
using GrpcMarketDataClient = InvestOps.MarketData.V1.MarketData.MarketDataClient;

namespace InvestOps.Infrastructure.MarketData;

public sealed class MarketDataGateway(GrpcMarketDataClient client) : IMarketDataGateway
{
    public async Task<QuoteDto> GetQuoteAsync(string productId, CancellationToken ct)
    {
        try
        {
            var quote = await client.GetQuoteAsync(new GetQuoteRequest { ProductId = productId }, cancellationToken: ct);
            return Map(quote);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            throw new MarketDataUnavailableException("Serviço de market data indisponível.");
        }
    }

    public async IAsyncEnumerable<QuoteDto> StreamQuotesAsync(
        IEnumerable<string> productIds, [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new StreamQuotesRequest();
        request.ProductIds.AddRange(productIds);
        using var call = client.StreamQuotes(request, cancellationToken: ct);
        await foreach (var quote in call.ResponseStream.ReadAllAsync(ct))
        {
            yield return Map(quote);
        }
    }

    private static QuoteDto Map(Quote quote) =>
        new(quote.ProductId, decimal.Parse(quote.Price, CultureInfo.InvariantCulture), quote.AsOf.ToDateTimeOffset());
}
