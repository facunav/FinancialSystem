namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Copia un PlanningMonth existente a otro período — ver
/// docs/Epics/Epica-PlanificacionMensual.md, sección 6.2. Todos los PlanningItem
/// del origen se duplican en el destino, vuelven a Pending, y quedan completamente
/// independientes del origen a partir de la copia.
/// </summary>
public sealed record CopyPlanningMonthCommand(Guid SourcePlanningMonthId, DateTime TargetPeriod);
