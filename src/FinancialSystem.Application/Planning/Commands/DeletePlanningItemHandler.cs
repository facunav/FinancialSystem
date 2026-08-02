using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

public sealed class DeletePlanningItemHandler
{
    private readonly IApplicationDbContext _db;

    public DeletePlanningItemHandler(IApplicationDbContext db) => _db = db;

    public async Task<DeletePlanningItemResult> Handle(
        DeletePlanningItemCommand command, CancellationToken cancellationToken = default)
    {
        var item = await _db.PlanningItems
            .FirstOrDefaultAsync(i => i.Id == command.PlanningItemId, cancellationToken);

        if (item is null)
            return DeletePlanningItemResult.NotFound();

        _db.PlanningItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);

        return DeletePlanningItemResult.Success();
    }
}
