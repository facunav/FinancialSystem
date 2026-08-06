namespace FinancialSystem.Application.Categories.Commands;

public enum CreateCategoryFailureReason
{
    /// <summary>Ya existe una Category con ese Name (calculado o provisto).</summary>
    NameAlreadyExists,
}

/// <summary>
/// Resultado de <see cref="CreateCategoryCommand"/>: éxito con la categoría creada, o
/// motivo de fallo. <see cref="ConflictingName"/> solo se completa en el caso de
/// conflicto -- es el Name ya calculado (derivado de DisplayName o provisto tal cual)
/// que el endpoint necesita para el mensaje de error, sin recalcularlo.
/// </summary>
public sealed record CreateCategoryResult(
    bool IsSuccess,
    CategorySummary? Category,
    CreateCategoryFailureReason? FailureReason,
    string? ConflictingName)
{
    public static CreateCategoryResult Success(CategorySummary category) => new(true, category, null, null);

    public static CreateCategoryResult NameConflict(string name) =>
        new(false, null, CreateCategoryFailureReason.NameAlreadyExists, name);
}
