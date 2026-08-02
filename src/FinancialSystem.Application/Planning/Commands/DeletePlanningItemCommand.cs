namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Elimina un PlanningItem. Solo afecta al PlanningMonth al que pertenece.</summary>
public sealed record DeletePlanningItemCommand(Guid PlanningItemId);
