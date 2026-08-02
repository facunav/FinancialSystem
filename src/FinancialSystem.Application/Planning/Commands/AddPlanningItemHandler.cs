using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Crea un PlanningItem en un PlanningMonth ya existente, siempre en estado
/// Pending — mismo patrón que AddInvestigationFindingHandler (chequeo de
/// existencia del padre con AnyAsync, sin cargarlo completo).
/// </summary>
public sealed class AddPlanningItemHandler
{
    private readonly IApplicationDbContext _db;

    public AddPlanningItemHandler(IApplicationDbContext db) => _db = db;

    public async Task<AddPlanningItemResult> Handle(
        AddPlanningItemCommand command, CancellationToken cancellationToken = default)
    {
        var planningMonthExists = await _db.PlanningMonths
            .AnyAsync(m => m.Id == command.PlanningMonthId, cancellationToken);
        if (!planningMonthExists)
            return AddPlanningItemResult.PlanningMonthNotFound();

        var item = new PlanningItem
        {
            PlanningMonthId = command.PlanningMonthId,
            Title = command.Title,
            ExpectedAmount = command.ExpectedAmount,
            DueDate = command.DueDate,
            IsPaid = false,
            PaidAt = null,
        };

        _db.PlanningItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        return AddPlanningItemResult.Success(item.Id);
    }
}
