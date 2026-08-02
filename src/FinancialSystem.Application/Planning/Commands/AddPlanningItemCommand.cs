namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Agrega un PlanningItem a un PlanningMonth existente. ExpectedAmount y DueDate son
/// opcionales — el usuario puede cargar el concepto antes de conocer el monto real
/// (ver docs/Epics/Epica-PlanificacionMensual.md, sección 5).
/// </summary>
public sealed record AddPlanningItemCommand(
    Guid PlanningMonthId, string Title, decimal? ExpectedAmount, DateTime? DueDate);
