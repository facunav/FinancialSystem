namespace FinancialSystem.Application.Categories;

/// <summary>
/// Forma de salida compartida por las queries/commands de Categories
/// (<see cref="Queries.GetCategoriesHandler"/>, <see cref="Commands.CreateCategoryHandler"/>,
/// <see cref="Commands.UpdateCategoryHandler"/>) — evita repetir el mismo record de
/// 5 campos tres veces. No es <c>FinancialSystem.Api.DTOs.CategoryDto</c>: Application
/// no depende de Api, cada endpoint mapea esto al DTO HTTP que corresponda.
/// </summary>
public sealed record CategorySummary(
    Guid Id,
    string Name,
    string DisplayName,
    int SortOrder,
    bool IsDeactivated);
