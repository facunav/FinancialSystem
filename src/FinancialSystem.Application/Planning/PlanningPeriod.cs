namespace FinancialSystem.Application.Planning;

/// <summary>
/// Normaliza un Period a las 00:00:00 UTC del primer día del mes. PlanningMonth
/// representa un mes, no un instante (ver docs/Epics/Epica-PlanificacionMensual.md,
/// sección 5: "Period — el mes al que corresponde esta planificación") — dos
/// llamados con distinta hora del mismo mes deben resolver siempre al mismo
/// PlanningMonth. Usado tanto por las queries (GetByPeriodAsync) como por los
/// comandos que crean un PlanningMonth (CreatePlanningMonthCommand,
/// CopyPlanningMonthCommand).
/// </summary>
public static class PlanningPeriod
{
    public static DateTime Normalize(DateTime period) =>
        new(period.Year, period.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}
