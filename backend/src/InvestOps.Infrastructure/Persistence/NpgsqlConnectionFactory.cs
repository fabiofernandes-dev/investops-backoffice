using System.Data.Common;
using InvestOps.Application.Abstractions;
using Npgsql;

namespace InvestOps.Infrastructure.Persistence;

public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
