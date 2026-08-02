namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Crea un PlanningMonth vacío para un período — caso "Empezar vacío" de la épica
/// (docs/Epics/Epica-PlanificacionMensual.md, sección 6.1). Sin PlanningItem
/// iniciales, sin ExpectedIncome (se carga aparte, ver UpdateExpectedIncomeCommand).
/// </summary>
public sealed record CreatePlanningMonthCommand(DateTime Period);
