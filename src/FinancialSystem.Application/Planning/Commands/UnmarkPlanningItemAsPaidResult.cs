namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Resultado de <see cref="UnmarkPlanningItemAsPaidCommand"/>.</summary>
public sealed record UnmarkPlanningItemAsPaidResult(bool IsSuccess)
{
    public static UnmarkPlanningItemAsPaidResult NotFound() => new(false);

    public static UnmarkPlanningItemAsPaidResult Success() => new(true);
}
