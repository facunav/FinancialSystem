namespace FinancialSystem.Application.Categories.Commands;

/// <summary>
/// Edita DisplayName y/o SortOrder de una Category existente. DisplayName nulo/vacío
/// significa "no cambiar este campo" (mismo criterio que ya validaba
/// UpdateCategoryRequestValidator) -- no es un error, ver <see cref="UpdateCategoryHandler"/>.
/// </summary>
public sealed record UpdateCategoryCommand(Guid CategoryId, string? DisplayName, int? SortOrder);
