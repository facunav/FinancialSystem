namespace FinancialSystem.Application.Planning.Commands;

public enum CreatePlanningMonthFailureReason
{
    /// <summary>Ya existe un PlanningMonth para ese período.</summary>
    PeriodAlreadyExists,
}

/// <summary>Resultado de <see cref="CreatePlanningMonthCommand"/>.</summary>
public sealed record CreatePlanningMonthResult(
    bool IsSuccess, Guid? PlanningMonthId, DateTime? Period, CreatePlanningMonthFailureReason? FailureReason)
{
    public static CreatePlanningMonthResult Success(Guid planningMonthId, DateTime period) =>
        new(true, planningMonthId, period, null);

    public static CreatePlanningMonthResult Failure(CreatePlanningMonthFailureReason reason) =>
        new(false, null, null, reason);
}
