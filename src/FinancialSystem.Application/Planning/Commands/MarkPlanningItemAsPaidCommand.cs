namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Marca un PlanningItem como pagado — ver docs/Epics/Epica-PlanificacionMensual.md,
/// sección 6.4. Siempre una acción manual y explícita del usuario.
/// </summary>
public sealed record MarkPlanningItemAsPaidCommand(Guid PlanningItemId);
