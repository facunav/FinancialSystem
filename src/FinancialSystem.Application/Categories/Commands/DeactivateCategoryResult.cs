namespace FinancialSystem.Application.Categories.Commands;

public enum DeactivateCategoryOutcome
{
    /// <summary>La Category se desactivó ahora.</summary>
    Deactivated,

    /// <summary>Ya estaba desactivada -- no se hizo ningún cambio.</summary>
    AlreadyDeactivated,
}

/// <summary>
/// Resultado de <see cref="DeactivateCategoryCommand"/>: éxito (con el desenlace y el
/// mismo texto de mensaje que ya devolvía el endpoint) o Category inexistente.
/// <see cref="Message"/> se arma acá para que el endpoint no tenga que reconstruirlo --
/// mismo texto exacto que antes en cada uno de los dos casos.
/// </summary>
public sealed record DeactivateCategoryResult(bool IsSuccess, DeactivateCategoryOutcome? Outcome, string? Message)
{
    public static DeactivateCategoryResult NotFound() => new(false, null, null);

    public static DeactivateCategoryResult Success(DeactivateCategoryOutcome outcome, string message) =>
        new(true, outcome, message);
}
