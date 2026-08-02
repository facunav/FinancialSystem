namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Resultado de <see cref="AddPlanningItemCommand"/>: éxito con el Id creado, o PlanningMonth inexistente.</summary>
public sealed record AddPlanningItemResult(bool PlanningMonthFound, Guid? PlanningItemId)
{
    public bool IsSuccess => PlanningMonthFound;

    public static AddPlanningItemResult PlanningMonthNotFound() => new(false, null);

    public static AddPlanningItemResult Success(Guid planningItemId) => new(true, planningItemId);
}
