// Wrapper extensions to keep Program.cs unchanged while bringing the
// actual infrastructure and reconciliation extensions into scope.
// Placing these in the namespace that Program.cs already imports
// (FinancialSystem.Application.Reconciliation) makes them available
// without modifying Program.cs.
namespace FinancialSystem.Application.Reconciliation
{
    public static class ApiCompatibilityExtensions
    {
        public static IServiceCollection AddOpenApi(this IServiceCollection services)
        {
            // Minimal implementation equivalent to what Program.cs expected.
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
            return services;
        }

        public static WebApplication MapOpenApi(this WebApplication app)
        {
            // No-op placeholder kept for compatibility with existing code.
            return app;
        }

        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Forward to the real implementation in FinancialSystem.Infrastructure
            return FinancialSystem.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);
        }

        public static IServiceCollection AddReconciliation(this IServiceCollection services, IConfiguration configuration)
        {
            // Forward to the real implementation in FinancialSystem.Infrastructure.Reconciliation
            return FinancialSystem.Infrastructure.Reconciliation.ReconciliationServiceExtensions.AddReconciliation(services, configuration);
        }
    }
}
