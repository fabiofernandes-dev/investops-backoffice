using System.Data.Common;

namespace InvestOps.Infrastructure.Persistence;

/// <summary>
/// Conexão (e transação corrente) compartilhada pelos repositórios dentro de um escopo.
/// A transação é controlada pelo <see cref="UnitOfWork"/>; os repos apenas usam a conexão/transação ativa.
/// </summary>
public interface IDbSession : IAsyncDisposable
{
    Task<DbConnection> GetConnectionAsync(CancellationToken ct);
    DbTransaction? Transaction { get; }
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
