namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Revierte MarkPlanningItemAsPaidCommand: vuelve el PlanningItem a Pending.</summary>
public sealed record UnmarkPlanningItemAsPaidCommand(Guid PlanningItemId);
