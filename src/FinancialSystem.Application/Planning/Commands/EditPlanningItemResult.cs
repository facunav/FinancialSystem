namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Resultado de <see cref="EditPlanningItemCommand"/>.</summary>
public sealed record EditPlanningItemResult(bool IsSuccess)
{
    public static EditPlanningItemResult NotFound() => new(false);

    public static EditPlanningItemResult Success() => new(true);
}
