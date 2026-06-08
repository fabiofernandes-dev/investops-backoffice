using Microsoft.Extensions.DependencyInjection;

namespace InvestOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Handlers e validators são registrados nas tasks seguintes (P2.2+).
        return services;
    }
}
