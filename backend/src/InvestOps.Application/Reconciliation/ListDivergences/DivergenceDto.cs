namespace InvestOps.Application.Reconciliation.ListDivergences;

public sealed record DivergenceDto(
    Guid Id, Guid ImportId, Guid AccountId, string CustomerName, string AccountNumber,
    Guid ProductId, string ProductName, string? Ticker,
    decimal InternalQuantity, decimal CustodianQuantity, decimal Difference,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt);
