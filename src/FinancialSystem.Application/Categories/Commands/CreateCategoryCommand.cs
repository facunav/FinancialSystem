namespace FinancialSystem.Application.Categories.Commands;

/// <summary>
/// Crea una Category nueva. Name es opcional -- si no se provee, se deriva de
/// DisplayName normalizado (ver <see cref="CreateCategoryHandler"/>).
/// </summary>
public sealed record CreateCategoryCommand(string DisplayName, string? Name);
