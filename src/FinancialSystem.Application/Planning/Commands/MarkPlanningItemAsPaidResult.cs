namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Resultado de <see cref="MarkPlanningItemAsPaidCommand"/>.</summary>
public sealed record MarkPlanningItemAsPaidResult(bool IsSuccess, DateTime? PaidAt)
{
    public static MarkPlanningItemAsPaidResult NotFound() => new(false, null);

    public static MarkPlanningItemAsPaidResult Success(DateTime paidAt) => new(true, paidAt);
}
