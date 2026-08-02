namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Resultado de <see cref="DeletePlanningItemCommand"/>.</summary>
public sealed record DeletePlanningItemResult(bool IsSuccess)
{
    public static DeletePlanningItemResult NotFound() => new(false);

    public static DeletePlanningItemResult Success() => new(true);
}
