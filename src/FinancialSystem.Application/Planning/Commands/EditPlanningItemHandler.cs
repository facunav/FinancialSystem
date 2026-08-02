using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

public sealed class EditPlanningItemHandler
{
    private readonly IApplicationDbContext _db;

    public EditPlanningItemHandler(IApplicationDbContext db) => _db = db;

    public async Task<EditPlanningItemResult> Handle(
        EditPlanningItemCommand command, CancellationToken cancellationToken = default)
    {
        var item = await _db.PlanningItems
            .FirstOrDefaultAsync(i => i.Id == command.PlanningItemId, cancellationToken);

        if (item is null)
            return EditPlanningItemResult.NotFound();

        item.Title = command.Title;
        item.ExpectedAmount = command.ExpectedAmount;
        item.DueDate = command.DueDate;

        await _db.SaveChangesAsync(cancellationToken);

        return EditPlanningItemResult.Success();
    }
}
