using FluentValidation;
using InvestOps.Application.Operations.GetOperationAudit;
using InvestOps.Application.Operations.GetOperationById;
using InvestOps.Application.Operations.GetOperationEvents;
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
        services.AddScoped<Operations.ReprocessOperation.ReprocessOperationHandler>();
        services.AddScoped<GetOperationByIdHandler>();
        services.AddScoped<ListOperationsHandler>();
        services.AddScoped<GetOperationEventsHandler>();
        services.AddScoped<Operations.ListProcessingEvents.ListProcessingEventsHandler>();
        services.AddScoped<GetOperationAuditHandler>();
        services.AddScoped<Positions.ListPositionsHandler>();
        services.AddScoped<Positions.GetPositionsByAccountHandler>();
        services.AddScoped<Positions.GetPositionsByCustomerHandler>();
        services.AddScoped<Dashboard.GetDashboardSummaryHandler>();
        services.AddScoped<Dashboard.GetProcessingMetricsHandler>();
        services.AddScoped<Lookups.ListProductsHandler>();
        services.AddScoped<Lookups.ListCustomersHandler>();
        services.AddScoped<Lookups.ListAccountsHandler>();
        services.AddScoped<Reconciliation.ImportReconciliation.ImportReconciliationHandler>();
        services.AddScoped<Reconciliation.ListDivergences.ListDivergencesHandler>();
        services.AddScoped<Reconciliation.ResolveDivergence.ResolveDivergenceHandler>();
        services.AddScoped<Reconciliation.IgnoreDivergence.IgnoreDivergenceHandler>();
        services.AddValidatorsFromAssemblyContaining<ImportOperationValidator>();
        return services;
    }
}
