namespace InvestOps.Application.Abstractions;

public interface IInvestmentAccountRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
}
