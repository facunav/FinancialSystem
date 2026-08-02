using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Ver docs/Epics/Epica-PlanificacionMensual.md, sección 6.2. Copia PlanningMonth +
/// todos sus PlanningItem: ExpectedAmount y DueDate se copian como referencia,
/// IsPaid/PaidAt se resetean (Pending, sin fecha de pago). Nunca se copia
/// ExpectedIncome — la épica no lo pide; es un dato que el usuario carga cada mes
/// (sección 5), no algo que deba heredarse.
///
/// Orden de los PlanningItem: PlanningItem (PR 0041) no tiene un campo de orden
/// persistido — se preserva el orden de lectura de la fuente tal como EF Core lo
/// devuelve, sin ORDER BY explícito. Es una limitación conocida y aceptada (la
/// épica optó deliberadamente por no agregar un campo SortOrder para mantener el
/// modelo mínimo), no un descuido de este handler.
/// </summary>
public sealed class CopyPlanningMonthHandler
{
    private readonly IApplicationDbContext _db;

    public CopyPlanningMonthHandler(IApplicationDbContext db) => _db = db;

    public async Task<CopyPlanningMonthResult> Handle(
        CopyPlanningMonthCommand command, CancellationToken cancellationToken = default)
    {
        var source = await _db.PlanningMonths
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Id == command.SourcePlanningMonthId, cancellationToken);

        if (source is null)
            return CopyPlanningMonthResult.Failure(CopyPlanningMonthFailureReason.SourceNotFound);

        var targetPeriod = PlanningPeriod.Normalize(command.TargetPeriod);

        var targetAlreadyExists = await _db.PlanningMonths
            .AnyAsync(m => m.Period == targetPeriod, cancellationToken);
        if (targetAlreadyExists)
            return CopyPlanningMonthResult.Failure(CopyPlanningMonthFailureReason.TargetPeriodAlreadyExists);

        var target = new PlanningMonth
        {
            Period = targetPeriod,
        };
        _db.PlanningMonths.Add(target);

        foreach (var item in source.Items)
        {
            _db.PlanningItems.Add(new PlanningItem
            {
                PlanningMonthId = target.Id,
                Title = item.Title,
                ExpectedAmount = item.ExpectedAmount,
                DueDate = item.DueDate,
                IsPaid = false,
                PaidAt = null,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return CopyPlanningMonthResult.Success(target.Id, target.Period, source.Items.Count);
    }
}
