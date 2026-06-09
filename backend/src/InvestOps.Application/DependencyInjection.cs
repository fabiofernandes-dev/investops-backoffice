using FluentValidation;
using InvestOps.Application.Operations.ImportOperation;
using Microsoft.Extensions.DependencyInjection;

namespace InvestOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ImportOperationHandler>();
        services.AddValidatorsFromAssemblyContaining<ImportOperationValidator>();
        return services;
    }
}
