using FluentValidation;
using InvestOps.Application.Operations.GetOperationById;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Application.Operations.ListOperations;
using Microsoft.Extensions.DependencyInjection;

namespace InvestOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ImportOperationHandler>();
        services.AddScoped<GetOperationByIdHandler>();
        services.AddScoped<ListOperationsHandler>();
        services.AddValidatorsFromAssemblyContaining<ImportOperationValidator>();
        return services;
    }
}
