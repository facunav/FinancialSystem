namespace FinancialSystem.Application.Categories.Commands;

/// <summary>Resultado de <see cref="UpdateCategoryCommand"/>: éxito con la categoría actualizada, o Category inexistente.</summary>
public sealed record UpdateCategoryResult(bool IsSuccess, CategorySummary? Category)
{
    public static UpdateCategoryResult NotFound() => new(false, null);

    public static UpdateCategoryResult Success(CategorySummary category) => new(true, category);
}
