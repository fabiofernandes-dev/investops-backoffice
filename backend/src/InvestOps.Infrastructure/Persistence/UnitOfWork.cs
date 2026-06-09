using InvestOps.Application.Abstractions;

namespace InvestOps.Infrastructure.Persistence;

public sealed class UnitOfWork(IDbSession session) : IUnitOfWork
{
    public Task BeginAsync(CancellationToken ct) => session.BeginTransactionAsync(ct);
    public Task CommitAsync(CancellationToken ct) => session.CommitAsync(ct);
    public Task RollbackAsync(CancellationToken ct) => session.RollbackAsync(ct);
}
