using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts.Commands;
using FinancialSystem.Application.Audit.Commands;
using FinancialSystem.Application.BankStatements.Commands;
using FinancialSystem.Application.Categories.Commands;
using FinancialSystem.Application.Categories.Queries;
using FinancialSystem.Application.Counterparties.Commands;
using FinancialSystem.Application.Counterparties.Queries;
using FinancialSystem.Application.Investigations.Commands;
using FinancialSystem.Application.Planning.Commands;
using FinancialSystem.Application.Review.Commands;
using FinancialSystem.Application.Transactions.Commands;
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
        services.AddScoped<GetCategoriesHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<UpdateCategoryHandler>();
        services.AddScoped<DeactivateCategoryHandler>();
        services.AddScoped<GetCounterpartiesHandler>();
        services.AddScoped<GetCounterpartyByIdHandler>();
        services.AddScoped<CreateCounterpartyHandler>();
        services.AddScoped<UpdateCounterpartyHandler>();
        services.AddScoped<DeactivateCounterpartyHandler>();
        services.AddScoped<CreateFinancialAccountHandler>();
        services.AddScoped<UpdateFinancialAccountHandler>();
        services.AddScoped<DeactivateFinancialAccountHandler>();
        services.AddScoped<ReactivateFinancialAccountHandler>();
        services.AddScoped<AssignTransactionFinancialAccountHandler>();
        services.AddScoped<AssignBankStatementFinancialAccountHandler>();
        return services;
    }

    private sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
