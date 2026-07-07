using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review.Commands;
using FinancialSystem.Application.Review.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<GetUnclassifiedMovementsHandler>();
        services.AddScoped<ClassifyMovementHandler>();
        services.AddScoped<ConfirmMatchHandler>();
        services.AddScoped<DiscardLegacyCandidatesHandler>();
        services.AddScoped<RestoreLegacyCandidatesHandler>();
        return services;
    }

    private sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
