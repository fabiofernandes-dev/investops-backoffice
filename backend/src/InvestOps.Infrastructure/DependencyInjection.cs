using InvestOps.Application.Abstractions;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Dapper;
using InvestOps.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvestOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperConfig.Register();

        var connectionString = configuration.GetConnectionString("Postgres")!;
        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        services.AddSingleton<IOperationRepository, OperationRepository>();
        services.AddSingleton<IFinancialProductRepository, FinancialProductRepository>();
        services.AddSingleton<IInvestmentAccountRepository, InvestmentAccountRepository>();
        services.AddSingleton<IOutboxRepository, OutboxRepository>();
        services.AddSingleton<IAuditRepository, AuditRepository>();

        return services;
    }
}
