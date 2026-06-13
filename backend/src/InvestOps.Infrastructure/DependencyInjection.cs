using InvestOps.Application.Abstractions;
using InvestOps.Infrastructure.Messaging;
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

        // Sessão e Unit of Work são scoped: repos compartilham a mesma conexão/transação no escopo.
        services.AddScoped<IDbSession, DbSession>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IOperationRepository, OperationRepository>();
        services.AddScoped<IFinancialProductRepository, FinancialProductRepository>();
        services.AddScoped<IInvestmentAccountRepository, InvestmentAccountRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IProcessingEventReadRepository, ProcessingEventReadRepository>();
        services.AddScoped<IPositionReadRepository, PositionReadRepository>();
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();

        var rabbitOptions = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        services.AddSingleton(rabbitOptions);
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        services.AddHostedService<OutboxPublisher>();

        return services;
    }
}
