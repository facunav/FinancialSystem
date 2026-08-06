namespace FinancialSystem.Application.Categories.Queries;

/// <summary>
/// Lista las categorías existentes, ordenadas por SortOrder y luego por DisplayName.
/// Por defecto excluye las desactivadas -- ver <see cref="GetCategoriesHandler"/>.
/// </summary>
public sealed record GetCategoriesQuery(bool IncludeDeactivated);
