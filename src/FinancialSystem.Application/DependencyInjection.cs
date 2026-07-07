using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<GetUnclassifiedMovementsHandler>();
        return services;
    }

    private sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
