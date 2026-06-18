using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Application.Reconciliation.Commands;
using FinancialSystem.Application.Reconciliation.Engine;
using FinancialSystem.Application.Reconciliation.Matching;
using FinancialSystem.Application.Reconciliation.Queries;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Infrastructure.Reconciliation
{
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
            services.AddScoped<IApplicationDbContext>(
                sp => sp.GetRequiredService<AppDbContext>());

            services.AddScoped<ReconciliationOrchestrator>();
            services.AddScoped<IReconciledExpenseRepository, ReconciledExpenseRepository>();
            services.AddScoped<ReconciliationConfirmationService>();
            services.AddScoped<MovementHydrationService>();

            // N↔M handlers
            services.AddScoped<ConfirmGroupHandler>();
            services.AddScoped<GetUnmatchedMovementsHandler>();

            return services;
        }
    }
}