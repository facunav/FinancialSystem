namespace FinancialSystem.Application.Planning.Commands;

public enum CopyPlanningMonthFailureReason
{
    /// <summary>No existe un PlanningMonth con el Id de origen indicado.</summary>
    SourceNotFound,

    /// <summary>Ya existe un PlanningMonth para el período de destino.</summary>
    TargetPeriodAlreadyExists,
}

/// <summary>Resultado de <see cref="CopyPlanningMonthCommand"/>.</summary>
public sealed record CopyPlanningMonthResult(
    bool IsSuccess,
    Guid? PlanningMonthId,
    DateTime? Period,
    int? ItemsCopied,
    CopyPlanningMonthFailureReason? FailureReason)
{
    public static CopyPlanningMonthResult Success(Guid planningMonthId, DateTime period, int itemsCopied) =>
        new(true, planningMonthId, period, itemsCopied, null);

    public static CopyPlanningMonthResult Failure(CopyPlanningMonthFailureReason reason) =>
        new(false, null, null, null, reason);
}
