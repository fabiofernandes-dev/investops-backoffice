using System.Data.Common;

namespace InvestOps.Infrastructure.Persistence;

public sealed class DbSession(IDbConnectionFactory connectionFactory) : IDbSession
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;

    public DbTransaction? Transaction => _transaction;

    public async Task<DbConnection> GetConnectionAsync(CancellationToken ct)
    {
        _connection ??= await connectionFactory.OpenConnectionAsync(ct);
        return _connection;
    }

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct);
        _transaction = await connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await _transaction.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
