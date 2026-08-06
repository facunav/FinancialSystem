namespace FinancialSystem.Application.Accounts.Commands;

public enum ReactivateFinancialAccountOutcome
{
    Reactivated,
    AlreadyActive,
}

public enum ReactivateFinancialAccountFailureReason
{
    NotFound,

    /// <summary>Ya existe otra cuenta ACTIVA con el mismo Name -- no se puede reactivar sin romper la invariante de unicidad.</summary>
    NameConflict,
}

/// <summary>
/// Message viene pre-formateado desde el Handler (igual que en Deactivate) para que el
/// Endpoint no tenga que recomponer el texto -- incluido en el caso de NameConflict,
/// donde también sirve como cuerpo de la respuesta 409.
/// </summary>
public sealed record ReactivateFinancialAccountResult(
    bool IsSuccess,
    ReactivateFinancialAccountOutcome? Outcome,
    string? Message,
    ReactivateFinancialAccountFailureReason? FailureReason)
{
    public static ReactivateFinancialAccountResult NotFound() =>
        new(false, null, null, ReactivateFinancialAccountFailureReason.NotFound);

    public static ReactivateFinancialAccountResult NameConflict(string message) =>
        new(false, null, message, ReactivateFinancialAccountFailureReason.NameConflict);

    public static ReactivateFinancialAccountResult Success(ReactivateFinancialAccountOutcome outcome, string message) =>
        new(true, outcome, message, null);
}
