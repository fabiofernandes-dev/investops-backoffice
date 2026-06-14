namespace InvestOps.Application.Quotes;

public sealed record QuoteDto(string ProductId, decimal Price, DateTimeOffset AsOf);
