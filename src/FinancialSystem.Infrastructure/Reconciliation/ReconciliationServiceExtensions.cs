// ══════════════════════════════════════════════════════════════════════
// ARCHIVO: src/FinancialSystem.Infrastructure/Reconciliation/ReconciliationServiceExtensions.cs
// CAMBIO:  Reemplazar el archivo completo con esta versión que agrega
//          ManualExpenseRepository y ReconciliationOrchestrator.
// ══════════════════════════════════════════════════════════════════════

using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Application.Reconciliation.Engine;
using FinancialSystem.Application.Reconciliation.Matching;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Infrastructure.Reconciliation;

public static class ReconciliationServiceExtensions
{
    public static IServiceCollection AddReconciliation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ReconciliationOptions>(
            configuration.GetSection(ReconciliationOptions.SectionName));

        services.AddSingleton<IMatchingRule, CurrencyConstraint>();
        services.AddSingleton<IMatchingRule, AmountMatchingRule>();
        services.AddSingleton<IMatchingRule, DateMatchingRule>();
        services.AddSingleton<IMatchingRule, DescriptionMatchingRule>();
        services.AddSingleton<IMatchingRule, PaymentMethodMatchingRule>();

        services.AddSingleton<IMatchScorer, MatchScorer>();
        services.AddSingleton<ISuspicionDetector, SuspicionDetector>();
        services.AddSingleton<IReconciliationEngine, ReconciliationEngine>();

        services.AddScoped<IManualExpenseRepository, ManualExpenseRepository>();

        services.AddScoped<ReconciliationOrchestrator>();

        // NUEVO
        services.AddScoped<IReconciledExpenseRepository, ReconciledExpenseRepository>();
        services.AddScoped<ReconciliationConfirmationService>();

        return services;
    }
}