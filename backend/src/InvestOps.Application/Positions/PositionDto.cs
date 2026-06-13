namespace InvestOps.Application.Positions;

public sealed record PositionDto(
    Guid Id,
    Guid AccountId,
    Guid ProductId,
    string CustomerName,
    string AccountNumber,
    string ProductName,
    string? Ticker,
    decimal Quantity,
    decimal AveragePrice,
    decimal GrossAmount,
    DateTime UpdatedAt);
