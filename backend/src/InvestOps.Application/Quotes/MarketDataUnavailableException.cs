namespace InvestOps.Application.Quotes;

public sealed class MarketDataUnavailableException(string message) : Exception(message);
