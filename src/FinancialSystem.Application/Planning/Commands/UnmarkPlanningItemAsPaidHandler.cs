using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

public sealed class UnmarkPlanningItemAsPaidHandler
{
    private readonly IApplicationDbContext _db;

    public UnmarkPlanningItemAsPaidHandler(IApplicationDbContext db) => _db = db;

    public async Task<UnmarkPlanningItemAsPaidResult> Handle(
        UnmarkPlanningItemAsPaidCommand command, CancellationToken cancellationToken = default)
    {
        var item = await _db.PlanningItems
            .FirstOrDefaultAsync(i => i.Id == command.PlanningItemId, cancellationToken);

        if (item is null)
            return UnmarkPlanningItemAsPaidResult.NotFound();

        item.IsPaid = false;
        item.PaidAt = null;

        await _db.SaveChangesAsync(cancellationToken);

        return UnmarkPlanningItemAsPaidResult.Success();
    }
}
