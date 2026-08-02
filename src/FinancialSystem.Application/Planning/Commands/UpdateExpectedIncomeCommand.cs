namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Actualiza ExpectedIncome de un PlanningMonth. Null es un valor válido — vacía el
/// ingreso esperado (ver docs/Epics/Epica-PlanificacionMensual.md, sección 5:
/// "opcional").
/// </summary>
public sealed record UpdateExpectedIncomeCommand(Guid PlanningMonthId, decimal? ExpectedIncome);
