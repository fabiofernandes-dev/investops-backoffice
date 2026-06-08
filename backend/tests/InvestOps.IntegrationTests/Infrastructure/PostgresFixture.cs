using InvestOps.Migrator;
using Testcontainers.PostgreSql;
using Xunit;

namespace InvestOps.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var result = MigrationRunner.Run(ConnectionString, includeSeed: false);
        if (!result.Successful) throw result.Error!;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
