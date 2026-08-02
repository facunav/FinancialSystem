namespace FinancialSystem.Application.Planning.Commands;

/// <summary>
/// Edita Title/ExpectedAmount/DueDate de un PlanningItem existente. No toca
/// IsPaid/PaidAt — ver MarkPlanningItemAsPaidCommand/UnmarkPlanningItemAsPaidCommand.
/// </summary>
public sealed record EditPlanningItemCommand(
    Guid PlanningItemId, string Title, decimal? ExpectedAmount, DateTime? DueDate);
