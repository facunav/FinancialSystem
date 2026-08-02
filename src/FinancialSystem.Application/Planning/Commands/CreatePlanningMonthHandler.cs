using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Crea el PlanningMonth y valida que no exista ya uno para el mismo período
/// normalizado — el índice único de la migración (PR 0041) es la red de seguridad
/// final; esta comprobación evita depender de una excepción de EF Core para un
/// caso esperable.
/// </summary>
public sealed class CreatePlanningMonthHandler
{
    private readonly IApplicationDbContext _db;

    public CreatePlanningMonthHandler(IApplicationDbContext db) => _db = db;

    public async Task<CreatePlanningMonthResult> Handle(
        CreatePlanningMonthCommand command, CancellationToken cancellationToken = default)
    {
        var period = PlanningPeriod.Normalize(command.Period);

        var alreadyExists = await _db.PlanningMonths
            .AnyAsync(m => m.Period == period, cancellationToken);
        if (alreadyExists)
            return CreatePlanningMonthResult.Failure(CreatePlanningMonthFailureReason.PeriodAlreadyExists);

        var month = new PlanningMonth
        {
            Period = period,
        };

        _db.PlanningMonths.Add(month);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatePlanningMonthResult.Success(month.Id, month.Period);
    }
}
