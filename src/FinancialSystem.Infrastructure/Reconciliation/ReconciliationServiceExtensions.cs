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

        // ── Reglas de matching ────────────────────────────────────
        services.AddSingleton<IMatchingRule, CurrencyConstraint>();
        services.AddSingleton<IMatchingRule, AmountMatchingRule>();
        services.AddSingleton<IMatchingRule, DateMatchingRule>();
        services.AddSingleton<IMatchingRule, DescriptionMatchingRule>();
        services.AddSingleton<IMatchingRule, PaymentMethodMatchingRule>();

        // ── Servicios core del motor ──────────────────────────────
        services.AddSingleton<IMatchScorer, MatchScorer>();
        services.AddSingleton<ISuspicionDetector, SuspicionDetector>();
        services.AddSingleton<IReconciliationEngine, ReconciliationEngine>();

        // ── Repositorio de gastos manuales ────────────────────────
        // Scoped porque usa IDbContextFactory internamente y crea
        // un DbContext por operación. No tiene estado entre llamadas.
        services.AddScoped<IManualExpenseRepository, ManualExpenseRepository>();

        // ── Orquestador ───────────────────────────────────────────
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<ReconciliationOrchestrator>();

        return services;
    }
}
