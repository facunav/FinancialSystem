using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Audit.Commands;
using FinancialSystem.Application.Investigations.Commands;
using FinancialSystem.Application.Planning.Commands;
using FinancialSystem.Application.Review.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<ClassifyMovementHandler>();
        services.AddScoped<CreateInvestigationHandler>();
        services.AddScoped<LinkMovementToInvestigationHandler>();
        services.AddScoped<AddInvestigationFindingHandler>();
        services.AddScoped<UpdateInvestigationStatusHandler>();
        services.AddScoped<ReviewMovementsHandler>();
        services.AddScoped<CreatePlanningMonthHandler>();
        services.AddScoped<CopyPlanningMonthHandler>();
        services.AddScoped<AddPlanningItemHandler>();
        services.AddScoped<EditPlanningItemHandler>();
        services.AddScoped<DeletePlanningItemHandler>();
        services.AddScoped<MarkPlanningItemAsPaidHandler>();
        services.AddScoped<UnmarkPlanningItemAsPaidHandler>();
        services.AddScoped<UpdateExpectedIncomeHandler>();
        return services;
    }

    private sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
