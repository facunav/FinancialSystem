using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Setea IsPaid=true y PaidAt=ahora — ver épica, sección 6.4 ("Al marcarlo, se
/// registra PaidAt"). Sin lógica adicional: no busca ni reclasifica ningún
/// movimiento en Movimientos (esa integración queda fuera del MVP, sección 9).
/// </summary>
public sealed class MarkPlanningItemAsPaidHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkPlanningItemAsPaidHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<MarkPlanningItemAsPaidResult> Handle(
        MarkPlanningItemAsPaidCommand command, CancellationToken cancellationToken = default)
    {
        var item = await _db.PlanningItems
            .FirstOrDefaultAsync(i => i.Id == command.PlanningItemId, cancellationToken);

        if (item is null)
            return MarkPlanningItemAsPaidResult.NotFound();

        var now = _dateTimeProvider.UtcNow;
        item.IsPaid = true;
        item.PaidAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return MarkPlanningItemAsPaidResult.Success(now);
    }
}
