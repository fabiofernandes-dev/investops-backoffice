using Dapper;
using InvestOps.Infrastructure.Persistence;
using InvestOps.IntegrationTests.Infrastructure;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ConnectionSmokeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Opens_connection_and_schema_is_applied()
    {
        var factory = new NpgsqlConnectionFactory(fixture.ConnectionString);
        await using var conn = await factory.OpenConnectionAsync();

        var tableCount = await conn.ExecuteScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema='public' and table_name in ('operations','outbox_messages','audit_logs');");

        Assert.Equal(3L, tableCount);
    }
}
